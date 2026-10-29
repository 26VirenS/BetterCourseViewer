import AppKit
import SwiftUI
import UniformTypeIdentifiers

/// To Do & Calendar (1.3.170: one place, the web's 2.99.112): a month as a calendar's grid — each day's work and events on
/// it in their calendar's colour, today marked — with the day picked listed beside it where there is room; a week day by
/// day over the hours; or the next three weeks as a list, the Agenda. On the calendars chosen (Canvas shows ten at most).
/// To Do beside it in every view (Screens/TodoPanel.swift), its rows dragged onto a day or a time to plan them there; its
/// button in the page's head hides and shows it. (1.3.170) The head as the web lays it out — the period's big title and
/// what it holds under it, ‹ › Today, then To Do, Find Appointment, Sync, Calendars and Week, Month, Agenda — none of it
/// in the window's toolbar any more; and the page holds still: the calendar and To Do fill the window, each scrolling
/// inside itself (a window too narrow or too short for that, and the page scrolls as before). ‹ and › — or ← and → once
/// the calendar has been clicked — move a month or a week, sliding the way they go; Today comes back. (1.2.3) An item
/// clicked — on the grid, in the day, the week or the list — grows its preview out of itself (Components/WorkPreview.swift);
/// a double-click opens it.
struct CalendarView: View {
    @EnvironmentObject private var engine: Engine
    /// Month, week or list, as the page keeps it (`calView`).
    @State private var mode = "month"
    @State private var anchor = CalendarView.monthStart(Date())
    @State private var weekStart = CalendarView.weekBegins(Date(), days: CalendarView.keptDays)
    @State private var selected = Calendar.current.startOfDay(for: Date())
    @State private var data: CalendarData?
    @State private var error: String?
    /// Why the last reading failed, once something was already shown.
    @State private var failure: String?
    @State private var loading = false
    /// The span last asked for: an answer for another (a move made since) is not shown.
    @State private var asked = ""
    @State private var modeRead = false
    /// The screenshot suite's -SimplCalView looked at (each Calendar drawn looks once).
    @State private var launchViewRead = false
    @State private var choosing = false
    /// (1.3.172) The screenshot suite's -SimplPlanEdit YES: a plan's popover opened at its Edit.
    private static let shotPlanEdit = UserDefaults.standard.bool(forKey: "SimplPlanEdit")
    /// (1.3.173) The screenshot suite's plan popover (-SimplPlanPopover) drawn in the window, over the calendar's top
    /// right: a popover closes as its app goes behind the suite's other copies of the app, and its Edit was never
    /// pictured open.
    private static let shotInline = UserDefaults.standard.bool(forKey: "SimplPlanPopover")
    /// (1.3.151) Find Appointment's popup (Appointments.swift); the screenshot suite's -SimplFindAppointment YES opens it.
    @State private var finding = UserDefaults.standard.bool(forKey: "SimplFindAppointment")
    /// (1.3.165) Sync's popup (SyncPopup.swift); the screenshot suite's -SimplSync YES opens it.
    @State private var syncing = UserDefaults.standard.bool(forKey: "SimplSync")
    /// (1.3.186) The calendars of Sync's accounts and those linked by their address that are shown, counted on Calendars'
    /// button with Canvas's; the screenshot suite's -SimplCalendars YES draws Calendars' popover in the window.
    @State private var linkedShown = 0
    private static let shotCalendars = UserDefaults.standard.bool(forKey: "SimplCalendars")
    /// (1.3.78) Where what is shown first came from, for the console's open line (OpenClock).
    @State private var source = "fetched"
    /// (1.3.80) What the live checks know of the span shown (its fingerprint: LiveRefresh).
    @State private var marks = LiveMarks()
    /// The way the last move went (the month slides in from that side): set a frame before the move itself, so the
    /// month leaving reads it as it leaves.
    @State private var dir = 1
    /// (1.3.147) The list's days, picked beside it: the next three weeks until another span is picked (kept).
    @State private var agenda = AgendaSpan.saved() ?? .standard
    /// The first day of a span being picked, while its last is still to come.
    @State private var agendaFrom: Date?
    @FocusState private var focused: Bool
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @Environment(\.pageWidth) private var pageWidth
    /// The width the calendar's views really have, measured where they are drawn on a page that scrolls: the page's own
    /// width counts the room under a sidebar the window draws over its content.
    @State private var roomWidth: CGFloat?
    private var room: CGFloat { roomWidth ?? pageWidth ?? 1000 }
    /// (1.3.167) A plan's block being dragged on the week: by how many days and minutes (a quarter of an hour at a time);
    /// `dropped`, let go and on its way to the page — it stays where it was let go until the week is read again.
    @State private var planDrag: PlanDrag?
    /// (1.3.185) Where the pointer has that block, to the point: watched by the block alone (PlanFollow).
    @State private var planFollow = PlanFollow()
    /// (1.3.172, the web's 2.99.113 and 2.99.114) A plan's block whose foot is being pulled: how long it would be;
    /// `dropped`, let go and on its way to the page. The plan whose popover is up (its settings, Edit). The plan a dragged
    /// block or a row of To Do is held over: let go there, it joins that plan's group. The foot the pointer is over.
    @State private var planStretch: PlanStretch?
    @State private var planOpen: String?
    @State private var joinOnto: String?
    /// (1.3.195) The plan whose block is held out over To Do beside the week (let go, it is unplanned), and the plans let
    /// go there, faded out while the page takes them off.
    /// (the screenshot suite's -SimplUnplanOver YES: To Do ringed as one would be, no block held)
    @State private var unplanOver: String? = UserDefaults.standard.bool(forKey: "SimplUnplanOver") ? "" : nil
    @State private var unplanned: Set<String> = []
    /// (1.3.195) To Do sits beside the calendar (the page holding still), not over it: a plan can be dragged onto it.
    @State private var todoBeside = false
    @State private var gripHover: String?
    /// (1.3.181) The up-and-down pointer pushed for a plan's foot (popped once, when the pointer has left it and no pull is on).
    @State private var stretchCursor = false
    /// (1.3.172) The screenshot suite's -SimplPlanPopover YES: a piece of work planned and its popover opened (once).
    @State private var shotPlanned = false
    /// (1.3.169, 1.3.170 in every view) To Do beside the calendar, on unless turned off; a row of it being dragged over a
    /// day of the week — the day and the minute it would be planned for — or over a day of the month or the agenda; and
    /// what came of the last one let go.
    @AppStorage(TodoShown.key) private var todoOn = true
    @State private var todoDrop: TodoDrop? = CalendarView.shotDrop
    @State private var dayDrop: Date?
    @State private var todoSaid: String?
    @State private var saidTicket = 0
    /// The width of a day of the week's grid, for a drag to say which day it is over.
    @State private var weekColumn: CGFloat = 0
    /// (1.3.170) The week: five days, Monday to Friday (on a Saturday, the next week's), or all seven from Sunday — Week
    /// pressed again offers them; kept.
    @AppStorage(CalendarView.daysKey) private var weekDays = 5
    /// (1.3.170) The whole day's hours: an hour's height, pinched (or ⌘- and ⌃-scrolled) larger or smaller and kept; how
    /// tall the hours' view is (the smallest hour fits the whole day in it); the pinch's start; the hours' own scroll
    /// view, kept across the week's redraws and moves (a reference, never watched here: only what waits at its edges
    /// follows its scroll).
    @State private var hourHeight = CalendarView.keptHour
    @State private var weekViewport: CGFloat = 0
    @State private var pinchFrom: CGFloat?
    @State private var hourTicket = 0
    @State private var weekScroll = WeekScroller()

    private static let modes = ["month", "week", "list"]
    private static let line = Color(nsColor: .separatorColor)
    /// A day as the page names it ("2026-10-08"), whatever calendar the Mac is set to.
    private static let dayKey: DateFormatter = {
        let f = DateFormatter()
        f.calendar = Calendar(identifier: .gregorian)
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()

    private static func monthStart(_ d: Date) -> Date {
        let cal = Calendar.current
        return cal.date(from: cal.dateComponents([.year, .month], from: d)) ?? cal.startOfDay(for: d)
    }

    /// The first day of the week a day is in, as the Mac is set (a Sunday, a Monday …): the month's rows.
    private static func firstOfWeek(_ d: Date) -> Date {
        let cal = Calendar.current
        let s = cal.startOfDay(for: d)
        let back = (cal.component(.weekday, from: s) - cal.firstWeekday + 7) % 7
        return cal.date(byAdding: .day, value: -back, to: s) ?? s
    }

    /// (1.3.170) The week's first day, as the web's has it: of seven days, the Sunday; of five, the Monday — a Saturday's
    /// (and a Sunday's) the Monday after.
    private static func weekBegins(_ d: Date, days: Int) -> Date {
        let cal = Calendar.current
        let s = cal.startOfDay(for: d)
        let weekday = cal.component(.weekday, from: s) // (1 a Sunday … 7 a Saturday)
        let sunday = cal.date(byAdding: .day, value: 1 - weekday, to: s) ?? s
        if days == 7 { return sunday }
        return cal.date(byAdding: .day, value: weekday == 7 ? 8 : 1, to: sunday) ?? s
    }

    /// (1.3.170) The week's days and an hour's height as kept (the screenshot suite gives them for a launch:
    /// -SimplCalWeekDays 7, -SimplCalHourHeight 20).
    private static let daysKey = "SimplCalWeekDays"
    private static let hourKey = "SimplCalHourHeight"
    private static var keptDays: Int { UserDefaults.standard.integer(forKey: daysKey) == 7 ? 7 : 5 }
    private static var keptHour: CGFloat {
        let v = UserDefaults.standard.double(forKey: hourKey)
        return v > 0 ? min(hourMost, max(hourLeast, CGFloat(v))) : 44
    }

    private var cal: Calendar { Calendar.current }

    var body: some View {
        Group {
            if let d = data {
                GeometryReader { g in
                    let pad: CGFloat = g.size.width < 700 ? 16 : 24
                    let inner = max(0, min(Self.widest, g.size.width - pad * 2))
                    if fills(room: inner, height: g.size.height) {
                        filled(d, room: inner, pad: pad)
                    } else {
                        scrolling(d)
                    }
                }
                .font(.sBody)
            } else {
                LoadState(error: error) { Task { await load(animated: false) } }
            }
        }
        .navigationTitle("To Do & Calendar")
        .navigationSubtitle(periodTitle)
        .openTimed("Calendar", ready: data != nil, source: source)
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                CanvasMenu(url: "/calendar", title: "Calendar")
            }
        }
        .floatingPopup("sync", title: "Sync", isPresented: $syncing, engine: engine) {
            SyncPopup { Task { await load(animated: true) } } // (a calendar linked, shown or hidden: the calendar shows it)
                .environmentObject(engine)
        }
        .floatingPopup("appointments", title: "Find Appointment", isPresented: $finding, engine: engine) {
            AppointmentsPopup { Task { await load(animated: true) } } // (a time reserved or given back: the calendar shows it)
                .environmentObject(engine)
        }
        .onAppear(perform: seed)
        .onDisappear { weekScroll.detach() }
        // (1.3.184) a calendar account's calendars read again, shown or hidden: laid over the calendar as they are now
        .onReceive(NotificationCenter.default.publisher(for: .simplCalendarAccountsSynced)) { _ in Task { await load(animated: true) } }
        .overlay(alignment: .topTrailing) {
            if Self.shotCalendars, let d = data {
                CalendarsPopover(choices: d.calendars, openSync: {}) {}
                    .environmentObject(engine)
                    .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Color.primary.opacity(0.1)))
                    .shadow(color: .black.opacity(0.18), radius: 18, y: 6)
                    .padding(.top, 64)
                    .padding(.trailing, 150)
            }
        }
        .overlay(alignment: .topTrailing) {
            if Self.shotInline, let id = planOpen, let ev = data?.events.first(where: { $0.id == id }) {
                PlanPopover(event: ev, startEditing: Self.shotPlanEdit) { planOpen = nil }
                    .environmentObject(engine)
                    .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 12, style: .continuous).strokeBorder(Color.primary.opacity(0.1)))
                    .shadow(color: .black.opacity(0.18), radius: 18, y: 6)
                    .padding(.top, 150)
                    .padding(.trailing, 28)
            }
        }
        .task(id: engine.dataVersion) {
            // (the screenshot suite: -SimplCalView week|list opens on that view, in place of the one kept — the kept one
            // then not read at all, so a read still on its way when the task starts again cannot undo it; read by each
            // Calendar drawn, once, as the window's screens are made again while the app's answers come in)
            var asked: String?
            if !launchViewRead {
                launchViewRead = true
                asked = UserDefaults.standard.string(forKey: "SimplCalView").flatMap { Self.modes.contains($0) ? $0 : nil }
            }
            if !modeRead {
                modeRead = true
                if asked == nil, let v = try? await engine.read("calView", as: OK.self).view, Self.modes.contains(v) { mode = v }
            }
            if let asked { mode = asked }
            await load(animated: false)
        }
        // (1.3.80: checked every minute while it is on show; an event or a due date that changed moves into place)
        .liveChecks("calendar", since: { marks.at }) { engine, age in try await liveCheck(engine, age) }
    }

    // MARK: - The page

    /// The widest the page's content is drawn, as every page's.
    private static let widest: CGFloat = 1480
    /// The room between To Do and the calendar.
    private static let todoGap: CGFloat = 14

    /// (1.3.170) The least a view needs across: a month's seven days, the week's days over the hours, the agenda's list.
    private var calendarLeast: CGFloat {
        switch mode {
        case "week": return weekLeast
        case "list": return 300
        default: return 406
        }
    }

    private var weekLeast: CGFloat { Self.gutter + CGFloat(weekDays) * Self.dayLeast }

    /// The page holds still — the calendar and To Do filling the window, each scrolling inside itself — once the window
    /// has the room for both side by side, and the height.
    private func fills(room: CGFloat, height: CGFloat) -> Bool {
        height >= 500 && room >= calendarLeast + (leftColumn ? TodoPanel.width + Self.todoGap : 0)
    }

    /// (1.3.171) The column beside the calendar: To Do's (when shown), or in the agenda the days it lists, picked on
    /// a month — the agenda being a list of the work already, To Do's own list is not shown beside it.
    private var leftColumn: Bool { mode == "list" || todoOn }

    /// The page holding still: its head, then To Do and the calendar side by side, as tall as the window leaves them.
    private func filled(_ d: CalendarData, room: CGFloat, pad: CGFloat) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            heading(d, room: room)
            if let note = notice(d) {
                noticeBar(note.text, problem: note.problem)
            }
            HStack(alignment: .top, spacing: Self.todoGap) {
                if mode == "list" {
                    agendaPicker
                        .frame(width: TodoPanel.width)
                        .frame(maxHeight: .infinity, alignment: .top)
                        .transition(.opacity)
                } else if todoOn {
                    TodoPanel(said: todoSaid)
                        .frame(width: TodoPanel.width)
                        .frame(maxHeight: .infinity)
                        .overlay { unplanHere }
                        .transition(.move(edge: .leading).combined(with: .opacity))
                }
                GeometryReader { c in
                    keyed(content(d, size: c.size))
                        .frame(width: c.size.width, height: c.size.height, alignment: .topLeading)
                }
            }
            .frame(maxHeight: .infinity)
            .animation(reduceMotion ? nil : Motion.snappy, value: todoOn)
            .animation(reduceMotion ? nil : Motion.snappy, value: mode == "list")
        }
        .padding(.horizontal, pad)
        .padding(.top, 20)
        .padding(.bottom, 16)
        .frame(maxWidth: Self.widest + pad * 2, maxHeight: .infinity, alignment: .top)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background(PageGround())
        .onAppear { todoBeside = true }
        .onDisappear { todoBeside = false }
    }

    /// A window too narrow or too short to hold still: the page scrolls, To Do over the calendar.
    private func scrolling(_ d: CalendarData) -> some View {
        Page {
            heading(d, room: room)
            if let note = notice(d) {
                noticeBar(note.text, problem: note.problem)
            }
            if mode == "list" {
                agendaPicker
                    .frame(maxWidth: 380, alignment: .leading)
                    .transition(.opacity)
            } else if todoOn {
                TodoPanel(said: todoSaid)
                    .frame(height: 340)
                    .transition(.opacity)
            }
            keyed(content(d, size: nil))
                .frame(maxWidth: .infinity, alignment: .leading)
                .background {
                    GeometryReader { g in
                        Color.clear
                            .onAppear { roomWidth = g.size.width }
                            .onChange(of: g.size.width) { _, w in roomWidth = w }
                    }
                }
        }
        .animation(reduceMotion ? nil : Motion.snappy, value: todoOn)
    }

    /// The calendar, taking ← and → once it has been clicked.
    private func keyed(_ v: some View) -> some View {
        v
            .focusable()
            .focused($focused)
            .focusEffectDisabled()
            .onKeyPress(.leftArrow) { step(-1) }
            .onKeyPress(.rightArrow) { step(1) }
    }

    // MARK: - The head

    /// (1.3.170, the web's 2.99.112) The period's big title and what it holds under it, ‹ › Today beside it; then To Do,
    /// Find Appointment, Sync, Calendars and Week, Month, Agenda at the line's end — their words given up for their
    /// symbols, and then a line of their own, as the page narrows.
    private func heading(_ d: CalendarData, room: CGFloat) -> some View {
        let fit = headLayout(room)
        return Group {
            if fit.oneLine {
                HStack(alignment: .center, spacing: 14) {
                    titleBlock(d)
                    pager
                    spinner
                    Spacer(minLength: 8)
                    tools(d, words: fit.words)
                }
            } else {
                VStack(alignment: .leading, spacing: 12) {
                    HStack(alignment: .center, spacing: 14) {
                        titleBlock(d)
                        pager
                        spinner
                        Spacer(minLength: 0)
                    }
                    HStack(spacing: 0) {
                        Spacer(minLength: 0)
                        tools(d, words: fit.words)
                    }
                }
            }
        }
        .animation(Motion.gentle, value: loading)
    }

    @ViewBuilder
    private var spinner: some View {
        if loading {
            ProgressView()
                .controlSize(.small)
                .transition(.opacity)
        }
    }

    /// How the head is laid out in `room`: on one line or two, the tools with their words or their symbols alone — by
    /// how wide the title is at its size and how wide the tools are (measured once, as drawn at the large control size).
    private func headLayout(_ room: CGFloat) -> (oneLine: Bool, words: Bool) {
        let title = max(Self.textWidth(periodTitle, NSFont.systemFont(ofSize: 34, weight: .bold)),
                        Self.textWidth(data.map(periodLine) ?? "", NSFont.systemFont(ofSize: 14)))
        let lead = title + 14 + Self.pagerWidth + 22
        let appointments = engine.onBrightspace ? 0 : 1
        let words = Self.toolsFixed + Self.wordsWidth + CGFloat(appointments) * Self.findWords
        let symbols = Self.toolsFixed + Self.symbolsWidth + CGFloat(appointments) * Self.findSymbol
        if lead + words <= room { return (true, true) }
        if lead + symbols <= room { return (true, false) }
        return (false, words <= room)
    }

    /// The widths the head is laid out by: ‹ › Today; To Do's button, Week Month Agenda and the room between the tools;
    /// Sync and Calendars with their words or without; Find Appointment with or without.
    private static let pagerWidth: CGFloat = 158
    private static let toolsFixed: CGFloat = 42 + 236 + 4 * 8
    private static let wordsWidth: CGFloat = 82 + 142
    private static let symbolsWidth: CGFloat = 42 + 70
    private static let findWords: CGFloat = 170
    private static let findSymbol: CGFloat = 42

    private static func textWidth(_ s: String, _ font: NSFont) -> CGFloat {
        ceil((s as NSString).size(withAttributes: [.font: font]).width)
    }

    /// The month's name, "October 2026" (the week's, "Oct – Nov 2026"; the agenda's days), and what it holds under it.
    private func titleBlock(_ d: CalendarData) -> some View {
        ScreenHeading(title: periodTitle, sub: periodLine(d))
            .lineLimit(1)
            .fixedSize()
            .contentTransition(.numericText())
    }

    /// What ‹ and › move by, as their help names it.
    private var stepWords: (back: String, on: String) {
        switch mode {
        case "week": return ("Previous Week", "Next Week")
        case "list": return ("Earlier Days", "Later Days")
        default: return ("Previous Month", "Next Month")
        }
    }

    /// (1.3.170) ‹ › Today beside the month's, the week's or the list's title, as the web has them.
    private var pager: some View {
        GlassGroup(spacing: 6) {
            HStack(spacing: 6) {
                Button {
                    shift(-1)
                } label: {
                    Label(stepWords.back, systemImage: "chevron.left")
                        .labelStyle(HoverWordsLabel())
                }
                .glassButton()
                .hoverName(stepWords.back)
                Button {
                    shift(1)
                } label: {
                    Label(stepWords.on, systemImage: "chevron.right")
                        .labelStyle(HoverWordsLabel())
                }
                .glassButton()
                .hoverName(stepWords.on)
                Button("Today") { goToday() }
                    .glassButton()
                    .keyboardShortcut("t", modifiers: .command)
                    .help("Today (⌘T)")
            }
            .controlSize(.large)
        }
    }

    /// To Do beside the calendar or not; Find Appointment, Sync and Calendars (how many are shown); and the view.
    private func tools(_ d: CalendarData, words: Bool) -> some View {
        let shown = d.calendars.filter(\.on).count + linkedShown
        return HStack(spacing: 8) {
            if mode != "list" { // (1.3.171: the agenda's column is its month, not To Do)
                Button {
                    withAnimation(reduceMotion ? nil : Motion.snappy) { todoOn.toggle() }
                } label: {
                    Label(todoOn ? "Hide To Do" : "Show To Do", systemImage: "sidebar.left")
                        .labelStyle(HoverWordsLabel())
                }
                .glassButton(tint: todoOn ? Theme.accent : nil)
                .hoverName(todoOn ? "Hide To Do" : "Show To Do")
                .accessibilityHint(todoOn ? "Hides To Do beside the calendar" : "Shows To Do beside the calendar, to drag work onto a day or a time")
                .accessibilityAddTraits(todoOn ? .isSelected : [])
            }
            if !engine.onBrightspace { // (Brightspace has no Scheduler)
                Button {
                    finding = true
                } label: {
                    // (1.3.171) a person with a clock — office hours, a time with someone — where a calendar with a clock
                    // was taken at a glance for Calendars' calendar with a tick
                    Label("Find Appointment", systemImage: "person.badge.clock")
                        .labelStyle(ToolLabel(words: words))
                }
                .glassButton()
                .hoverName("Find Appointment", when: !words)
                .accessibilityHint("Office hours, conferences and sign-ups with a time to reserve")
            }
            Button {
                syncing = true
            } label: {
                Label("Sync", systemImage: "arrow.triangle.2.circlepath")
                    .labelStyle(ToolLabel(words: words))
            }
            .glassButton()
            .hoverName("Sync", when: !words)
            .accessibilityHint("Connect Apple, Google or Outlook calendars, and send Simpl to them")
            Button {
                choosing.toggle()
            } label: {
                HStack(spacing: 6) {
                    Label("Calendars", systemImage: "calendar.badge.checkmark")
                        .labelStyle(ToolLabel(words: words))
                    Text("\(shown)")
                        .font(.sCaption.weight(.semibold).monospacedDigit())
                        .foregroundStyle(Theme.accentText)
                        .padding(.horizontal, 6)
                        .frame(minWidth: 20, minHeight: 18)
                        .background(Capsule().fill(Theme.accent.opacity(0.15)))
                        .contentTransition(.numericText(value: Double(shown)))
                }
            }
            .glassButton()
            .hoverName("Calendars", when: !words && !choosing)
            .accessibilityHint("Choose the calendars shown (\(shown) shown)")
            .popover(isPresented: $choosing) {
                CalendarsPopover(choices: data?.calendars ?? [], openSync: openSync) { Task { await load(animated: true) } }
                    .environmentObject(engine)
            }
            CalendarModeSwitch(mode: mode, weekDays: weekDays, pick: setMode, weekMenu: weekMenu)
        }
        .controlSize(.large)
    }

    /// (1.3.170) Week pressed again: its five days or seven, the one in force ticked.
    private func weekMenu(_ under: NSView?) {
        PopMenu.show([
            .choice("5 days — Monday to Friday", on: weekDays == 5, act: { setWeekDays(5) }),
            .choice("7 days — Sunday to Saturday", on: weekDays == 7, act: { setWeekDays(7) }),
        ], under: under)
    }

    /// The week's five days or seven, kept: the same week, from its Monday or its Sunday.
    private func setWeekDays(_ n: Int) {
        guard n != weekDays else { return }
        let from = n == 5 ? (cal.date(byAdding: .day, value: 1, to: weekStart) ?? weekStart) : weekStart
        weekDays = n
        withAnimation(Motion.gentle) { weekStart = Self.weekBegins(from, days: n) }
        Task { await load(animated: true) }
    }

    /// (1.2) Under the heading: how much the month or the week holds and how much of it is missing ("14 items · 2
    /// missing"); the list's span.
    private func periodLine(_ d: CalendarData) -> String {
        if mode == "list" && agenda.isStandard { return "The next three weeks" }
        // (1.3.167) the week's days before what it holds ("Oct 26 – 30 · 16 items"): its title names only its month
        if mode == "week" {
            let end = cal.date(byAdding: .day, value: weekDays - 1, to: weekStart) ?? weekStart
            let days = "\(weekStart.formatted(.dateTime.month(.abbreviated).day())) – \(cal.isDate(weekStart, equalTo: end, toGranularity: .month) ? end.formatted(.dateTime.day()) : end.formatted(.dateTime.month(.abbreviated).day()))"
            return "\(days) · \(periodCount(d))"
        }
        return periodCount(d)
    }

    private func periodCount(_ d: CalendarData) -> String {
        let month = String(Self.dayKey.string(from: anchor).prefix(7))
        let shown = mode == "month" ? d.events.filter { $0.day.hasPrefix(month) } : d.events
        guard !shown.isEmpty else { return mode == "month" ? "Nothing this month" : mode == "week" ? "Nothing this week" : "Nothing in these days" }
        let missing = shown.filter { $0.missing == true }.count
        var parts = ["\(shown.count) \(shown.count == 1 ? "item" : "items")"]
        if missing > 0 { parts.append("\(missing) missing") }
        return parts.joined(separator: " · ")
    }

    /// "October 2026", "Oct – Nov 2026" (a week across two months), "Upcoming" (the next three weeks) or the days picked
    /// for the list.
    private var periodTitle: String {
        switch mode {
        case "list":
            guard !agenda.isStandard else { return "Upcoming" }
            let f = DateIntervalFormatter()
            f.dateStyle = .medium
            f.timeStyle = .none
            return f.string(from: agenda.start, to: agenda.last)
        case "week":
            // (1.3.167) named by its month, as Calendar names a week ("October 2026", "Oct – Nov 2026"): the title as
            // wide in every view, its days in the line under it
            let end = cal.date(byAdding: .day, value: weekDays - 1, to: weekStart) ?? weekStart
            if cal.isDate(weekStart, equalTo: end, toGranularity: .month) { return weekStart.formatted(.dateTime.month(.wide).year()) }
            let from = cal.isDate(weekStart, equalTo: end, toGranularity: .year) ? weekStart.formatted(.dateTime.month(.abbreviated)) : weekStart.formatted(.dateTime.month(.abbreviated).year())
            return "\(from) – \(end.formatted(.dateTime.month(.abbreviated).year()))"
        default:
            return anchor.formatted(.dateTime.month(.wide).year())
        }
    }

    /// What the page says over the calendar (no calendar chosen), or why it could not be read.
    private func notice(_ d: CalendarData) -> (text: String, problem: Bool)? {
        if let failure, !failure.isEmpty { return (failure, true) }
        if let e = d.error, !e.isEmpty { return (e, true) }
        if let n = d.notice, !n.isEmpty { return (n, false) }
        return nil
    }

    private func noticeBar(_ text: String, problem: Bool) -> some View {
        HStack(spacing: 10) {
            Image(systemName: problem ? "exclamationmark.triangle.fill" : "info.circle")
                .foregroundStyle(problem ? Color.orange : Color.secondary)
            Text(text)
                .font(.sCallout)
                .foregroundStyle(.secondary)
                .textSelection(.enabled)
            Spacer(minLength: 8)
            if problem {
                Button("Try Again") { Task { await load(animated: true) } }
                    .glassButton()
            } else {
                Button("Choose Calendars…") { choosing = true }
                    .glassButton()
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
        .card(radius: 14)
        .transition(.opacity)
    }

    // MARK: - The views

    /// The view chosen: at `size` (the calendar's column of a page that holds still, each part scrolling inside it) or,
    /// nil, on a page that scrolls.
    @ViewBuilder
    private func content(_ d: CalendarData, size: CGSize?) -> some View {
        let byDay = Dictionary(grouping: d.events, by: { $0.day })
        switch mode {
        case "week":
            weekView(byDay, size: size)
                .transition(.opacity)
        case "list":
            listView(byDay, size: size)
                .transition(.opacity)
        default:
            monthView(byDay, size: size)
                .transition(.opacity)
        }
    }

    /// A month moves in from the side the move went; under Reduce Motion it cross-fades.
    private var slide: AnyTransition {
        reduceMotion ? .opacity : .push(from: dir > 0 ? .trailing : .leading)
    }

    private func key(_ day: Date) -> String { Self.dayKey.string(from: day) }

    // MARK: Month

    /// The day panel's width beside the month, and the least the grid keeps beside it.
    private static let dayWidth: CGFloat = 300
    private static let gridLeast: CGFloat = 476
    /// (1.3.170) The least a week of the month is tall on a page that holds still: past that, its weeks scroll.
    private static let weekRowLeast: CGFloat = 76

    /// The month's grid with the day picked beside it on a wide window, under it on a narrow one that scrolls. (1.3.78)
    /// Chosen by the width, not by building both and measuring them: the grid is the heaviest thing drawn. (1.3.147) On a
    /// page that scrolls, the grid's weeks as tall as its days are wide (84 to 118 points), so a narrow window is not
    /// given a month taller than wide; the day beside it from a grid of 476 points. (1.3.170) On a page that holds still,
    /// the grid as tall as the window leaves it, its weeks stretched to fill it (scrolling inside it when they cannot),
    /// the day beside it only where there is room for both; else a day's "2 more" opens it in the Agenda.
    @ViewBuilder
    private func monthView(_ byDay: [String: [CalEvent]], size: CGSize?) -> some View {
        let width = size?.width ?? room
        let beside = width >= Self.gridLeast + 20 + Self.dayWidth
        let gridWidth = beside ? width - 20 - Self.dayWidth : width
        if let size {
            let weeks = CGFloat(weekCount())
            let fit = ((size.height - Self.monthHead - (weeks - 1)) / weeks).rounded(.down) // (less the hairlines between)
            let row = max(Self.weekRowLeast, fit)
            HStack(alignment: .top, spacing: 20) {
                monthGrid(byDay, row: row, scrolls: fit < Self.weekRowLeast, beside: beside)
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
                if beside {
                    dayPanel(selected, events: byDay[key(selected)] ?? [], fills: true)
                        .frame(width: Self.dayWidth)
                }
            }
        } else {
            let row = min(118, max(84, (gridWidth / 7).rounded(.down)))
            let total = row * 6
            let per = (total / CGFloat(weekCount())).rounded(.down)
            if beside {
                HStack(alignment: .top, spacing: 20) {
                    monthGrid(byDay, row: per, scrolls: false, beside: true)
                        .frame(maxWidth: .infinity)
                    dayPanel(selected, events: byDay[key(selected)] ?? [], fills: false)
                        .frame(width: Self.dayWidth)
                }
            } else {
                VStack(alignment: .leading, spacing: 22) {
                    monthGrid(byDay, row: per, scrolls: false, beside: true)
                    dayPanel(selected, events: byDay[key(selected)] ?? [], fills: false)
                }
            }
        }
    }

    /// The weekdays' names over the grid, and the hairline under them.
    private static let monthHead: CGFloat = 38

    /// `row`: each week's height; `scrolls`: the weeks scroll inside the grid (a page that holds still, too short for
    /// them); `beside`: the day picked is listed with the month (else a day's "2 more" opens it in the Agenda).
    private func monthGrid(_ byDay: [String: [CalEvent]], row: CGFloat, scrolls: Bool, beside: Bool) -> some View {
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                ForEach(Array(weekdaySymbols.enumerated()), id: \.offset) { _, s in
                    Text(s.uppercased())
                        .font(.sFootnote.weight(.semibold))
                        .tracking(0.6)
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, alignment: .trailing)
                        .padding(.trailing, 12)
                }
            }
            .padding(.vertical, 10)
            .accessibilityHidden(true)
            Self.line.frame(height: 1)
            ZStack(alignment: .top) {
                Group {
                    if scrolls {
                        ScrollView { monthWeeks(byDay, row: row, beside: beside) }
                            .scrollIndicators(.automatic)
                    } else {
                        monthWeeks(byDay, row: row, beside: beside)
                    }
                }
                .id(anchor) // (a new month is a new grid: it slides in from the side the move went)
                .transition(slide)
            }
            .clipped()
        }
        .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
        .card()
    }

    /// The days of the week in the Mac's order ("SUN MON …" or "MON TUE …").
    private var weekdaySymbols: [String] {
        let s = cal.shortWeekdaySymbols
        guard s.count == 7 else { return s }
        let first = (cal.firstWeekday - 1 + 7) % 7
        return Array(s[first...] + s[..<first])
    }

    /// (planning-23) The weeks the month touches — five or six, four for a February that starts on the week's first day —
    /// counted from the first day of the week it starts in. A row of the next month's days is never drawn.
    private func weekCount() -> Int {
        let first = Self.firstOfWeek(anchor)
        let next = cal.date(byAdding: .month, value: 1, to: anchor) ?? anchor
        let days = cal.dateComponents([.day], from: first, to: next).day ?? 42
        return min(6, max(4, (days + 6) / 7))
    }

    /// The month's weeks from the first day of the week it starts in, each `row` tall.
    private func monthWeeks(_ byDay: [String: [CalEvent]], row: CGFloat, beside: Bool) -> some View {
        let first = Self.firstOfWeek(anchor)
        let weeks = weekCount()
        let days = (0..<(weeks * 7)).compactMap { cal.date(byAdding: .day, value: $0, to: first) }
        return VStack(spacing: 0) {
            ForEach(0..<weeks, id: \.self) { w in
                if w > 0 { Self.line.frame(height: 1) }
                HStack(spacing: 0) {
                    ForEach(Array(days.dropFirst(w * 7).prefix(7).enumerated()), id: \.element) { i, day in
                        dayCell(day, events: byDay[key(day)] ?? [], height: row, beside: beside)
                            .overlay(alignment: .leading) {
                                if i > 0 { Self.line.frame(width: 1) }
                            }
                    }
                }
            }
        }
    }

    /// A day of the month: its number (today on the accent, the day picked on its wash), and its items as lines in their
    /// calendar's colour — (1.2) flat on the grid, no box round each. (planning-13) The open ones first, as many as the
    /// day has room for, a name taking a second line while there is room for it; work handed in folded into one line
    /// ("3 done", with what did not fit: "2 more · 3 done"), so the names still to do get the room. (1.3.170) A row of To
    /// Do held over it rings it, "Plan here" in it; let go, the work is planned at the day's first free time.
    private func dayCell(_ day: Date, events evs: [CalEvent], height: CGFloat, beside: Bool) -> some View {
        let off = !cal.isDate(day, equalTo: anchor, toGranularity: .month)
        let isToday = cal.isDateInToday(day)
        let isSel = cal.isDate(day, inSameDayAs: selected)
        let open = evs.filter { !$0.isStruck }
        let done = evs.count - open.count
        // (lines of about 18 points under the day's number: four in a day of 112 points, five in one of 134)
        let lines = max(2, Int(((height - 39 + 2) / 18.3).rounded(.down)))
        let tail = done > 0 || open.count > lines
        let room = lines - (tail ? 1 : 0)
        let shown = Array(open.prefix(room))
        let spare = room - shown.count
        let hidden = open.count - shown.count
        let rest = [hidden > 0 ? "\(hidden) more" : nil, done > 0 ? "\(done) done" : nil].compactMap { $0 }.joined(separator: " · ")
        let over = dayDrop.map { cal.isDate($0, inSameDayAs: day) } ?? false
        return Button {
            pick(day)
        } label: {
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Spacer(minLength: 0)
                    Text("\(cal.component(.day, from: day))")
                        .font(.sBody.weight(isToday || isSel ? .semibold : .regular).monospacedDigit())
                        .foregroundStyle(isToday ? Color.white : (isSel ? Theme.accent : (off ? Color.secondary : Color.primary)))
                        .frame(minWidth: 26, minHeight: 26)
                        .background {
                            if isToday {
                                Circle().fill(Theme.accent)
                            } else if isSel {
                                Circle().fill(Theme.accent.opacity(0.16))
                            }
                        }
                }
                VStack(alignment: .leading, spacing: 2) {
                    // (1.2.3) an item clicked on the grid grows its preview out of it; the rest of the day picks the day
                    ForEach(Array(shown.enumerated()), id: \.element.id) { i, ev in
                        eventLink(ev, padded: false, radius: 5) {
                            CalendarChip(event: ev, lines: i < spare ? 2 : 1)
                        }
                        .help(ev.title)
                    }
                    if tail {
                        if beside || hidden == 0 {
                            Text(rest)
                                .font(.sCaption.weight(.medium))
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                                .padding(.leading, 11)
                        } else {
                            // (no day beside the month: its whole list in the Agenda, as the web's "2 more")
                            Button { showDay(day) } label: {
                                Text(rest)
                                    .font(.sCaption.weight(.semibold))
                                    .foregroundStyle(Theme.accentText)
                                    .lineLimit(1)
                                    .padding(.leading, 11)
                                    .contentShape(Rectangle())
                            }
                            .buttonStyle(.plain)
                            .help("Everything on \(Self.dateWords(day, weekday: true))")
                        }
                    }
                }
                .opacity(off ? 0.6 : 1)
                Spacer(minLength: 0)
            }
            .padding(5)
            .frame(maxWidth: .infinity, minHeight: height, maxHeight: height, alignment: .topLeading)
            // (1.3.147: the days of the months either side a shade deeper, so the month's own stand out)
            .background(over ? Theme.accent.opacity(0.12) : (isSel ? Theme.accent.opacity(0.07) : Color.primary.opacity(off ? 0.035 : 0)))
            .overlay {
                if over { Rectangle().strokeBorder(Theme.accent, lineWidth: 2) }
            }
            .overlay(alignment: .bottomLeading) {
                if over { planHereTag.padding(6) }
            }
            .animation(Motion.snappy, value: isSel)
            .animation(reduceMotion ? nil : .easeOut(duration: 0.1), value: over)
        }
        .buttonStyle(RowButtonStyle(radius: 0))
        .onDrop(of: [.text], delegate: TodoDayDropDelegate(day: day, enabled: todoOn, over: $dayDrop) { id in planOnDay(id, day) })
        .accessibilityLabel(Self.spoken(day, count: evs.count))
        .accessibilityAddTraits(isSel ? .isSelected : [])
    }

    /// (1.3.195, the web's 2.99.114) To Do with a plan's block held over it: ringed, and a tag saying letting go there
    /// unplans it.
    @ViewBuilder
    private var unplanHere: some View {
        if unplanOver != nil {
            ZStack {
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .fill(Theme.accent.opacity(0.08))
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .strokeBorder(Theme.accent, lineWidth: 2)
                Label("Unplan", systemImage: "calendar.badge.minus")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(.white)
                    .padding(.horizontal, 10)
                    .padding(.vertical, 4)
                    .background(Capsule().fill(Theme.accent))
                    .fixedSize()
            }
            .allowsHitTesting(false)
            .transition(.opacity)
        }
    }

    /// (1.3.170) Over a day a row of To Do is held over: where letting go plans it.
    private var planHereTag: some View {
        Text("Plan here")
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(.white)
            .padding(.horizontal, 8)
            .padding(.vertical, 2)
            .background(Capsule().fill(Theme.accent))
            .fixedSize()
            .allowsHitTesting(false)
            .accessibilityHidden(true)
    }

    private static func spoken(_ day: Date, count: Int) -> String {
        let d = day.formatted(.dateTime.weekday(.wide).month(.wide).day())
        return count == 0 ? "\(d), nothing" : "\(d), \(count) \(count == 1 ? "item" : "items")"
    }

    // MARK: A day

    /// The day picked beside the month: its name, how much is on it and New Task over one card of its work and events —
    /// (1.3.170) `fills`: as tall as the month beside it, its card scrolling inside.
    private func dayPanel(_ day: Date, events evs: [CalEvent], fills: Bool) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .center, spacing: 10) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(day.formatted(.dateTime.weekday(.wide)))
                        .font(.sTitle3)
                    Text("\(Self.dateWords(day)) · \(countLine(day, evs.count))")
                        .font(.sCallout)
                        .foregroundStyle(.secondary)
                        .contentTransition(.numericText())
                }
                Spacer(minLength: 8)
                Button {
                    engine.newTask(on: day)
                } label: {
                    Label("New Task", systemImage: "plus")
                }
                .glassButton()
                .help("A task of your own, due this day (it shows in To Do)")
            }
            if fills {
                ScrollView { dayRows(evs) }
                    .scrollIndicators(.automatic)
                    .frame(maxHeight: .infinity, alignment: .top)
            } else {
                dayRows(evs)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: fills ? .infinity : nil, alignment: .topLeading)
    }

    /// A day's work and events in one card, each opening where it lives.
    private func dayRows(_ evs: [CalEvent]) -> some View {
        VStack(alignment: .leading, spacing: 0) {
            if evs.isEmpty {
                EmptyNote(text: "Nothing on this day.", symbol: "calendar")
                    .padding(.horizontal, 8)
            }
            ForEach(Array(evs.enumerated()), id: \.element.id) { i, ev in
                VStack(spacing: 0) {
                    if i > 0 { RowDivider() }
                    eventRow(ev)
                }
                .transition(.opacity)
            }
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
    }

    /// A day of the week or of the list: its name as the section's heading ("Tue, Oct 13"), its work and events in one card
    /// under it. (1.3.170) A row of To Do held over it rings it, "Plan here" at its head; let go, the work is planned at
    /// the day's first free time.
    private func dayCard(_ day: Date, events evs: [CalEvent]) -> some View {
        let over = dayDrop.map { cal.isDate($0, inSameDayAs: day) } ?? false
        return PageSection(title: Self.dateWords(day, weekday: true), trailing: countLine(day, evs.count)) {
            dayRows(evs)
        }
        .padding(over ? 8 : 0)
        .background {
            if over { RoundedRectangle(cornerRadius: 18, style: .continuous).fill(Theme.accent.opacity(0.08)) }
        }
        .overlay {
            if over { RoundedRectangle(cornerRadius: 18, style: .continuous).strokeBorder(Theme.accent, lineWidth: 2) }
        }
        .overlay(alignment: .topTrailing) {
            if over { planHereTag.padding(10) }
        }
        .padding(over ? -8 : 0)
        .animation(reduceMotion ? nil : .easeOut(duration: 0.1), value: over)
        .onDrop(of: [.text], delegate: TodoDayDropDelegate(day: day, enabled: todoOn, over: $dayDrop) { id in planOnDay(id, day) })
    }

    /// (planning-28) A day as every date is written: "Oct 13", "Tue, Oct 13" with its weekday; the year only when it is
    /// not this one ("Jan 5, 2027").
    private static func dateWords(_ day: Date, weekday: Bool = false) -> String {
        let thisYear = Calendar.current.isDate(day, equalTo: Date(), toGranularity: .year)
        let format = (weekday ? "EEE, " : "") + (thisYear ? "MMM d" : "MMM d, yyyy")
        return dateFormats[format]?.string(from: day) ?? day.formatted(date: .abbreviated, time: .omitted)
    }

    /// The four ways `dateWords` writes a day, made once.
    private static let dateFormats: [String: DateFormatter] = Dictionary(uniqueKeysWithValues:
        ["MMM d", "MMM d, yyyy", "EEE, MMM d", "EEE, MMM d, yyyy"].map { format -> (String, DateFormatter) in
            let f = DateFormatter()
            f.locale = Locale(identifier: "en_US_POSIX")
            f.dateFormat = format
            return (format, f)
        })

    /// "Today · 3 items", "Tomorrow · 1 item", "2 items".
    private func countLine(_ day: Date, _ n: Int) -> String {
        let rel = cal.isDateInToday(day) ? "Today · " : cal.isDateInTomorrow(day) ? "Tomorrow · " : ""
        return "\(rel)\(n) \(n == 1 ? "item" : "items")"
    }

    /// (1.2.3) A press grows the item's preview out of its row (a double-click opens it where it lives).
    private func eventRow(_ ev: CalEvent) -> some View {
        withEventMenu(ev) {
            eventLink(ev) {
                CalendarEventLine(event: ev)
            }
        }
    }

    /// An item's own menu (open it, open it in Canvas, copy its link), where it has an address.
    @ViewBuilder
    private func withEventMenu<V: View>(_ ev: CalEvent, @ViewBuilder _ content: () -> V) -> some View {
        if (ev.url ?? "").isEmpty && ev.reservationId == nil && ev.layer != "plan" {
            content()
        } else {
            content().contextMenu { eventMenu(ev) }
        }
    }

    @ViewBuilder
    private func eventMenu(_ ev: CalEvent) -> some View {
        if let url = ev.url, !url.isEmpty {
            Button("Open") { engine.openWeb(url, title: ev.title) }
            Divider()
            Button("Open in \(engine.lmsName)") { engine.openWebScreen(url, title: ev.title) }
            Button("Copy Link") { if let u = engine.absolute(url) { copyToPasteboard(u.absoluteString) } }
        }
        // (1.3.165) a plan: cleared from here (moved from its work's row); (1.3.172) edited, or deleted with its group
        if ev.layer == "plan", let pid = ev.planId {
            Divider()
            Button("Edit Plan…") { planOpen = ev.id }
            Button("Delete Plan") {
                Task {
                    _ = try? await engine.call("planClear", ["id": pid, "group": true], as: ActReply.self)
                    engine.changed()
                }
            }
        }
        // (1.3.151) a time reserved off Find Appointment: given back from here too, or other times found
        if let res = ev.reservationId {
            Divider()
            Button("Give Back This Time") { Task { await giveBack(res) } }
            Button("Find Another Time…") { finding = true }
        }
    }

    private func giveBack(_ reservation: String) async {
        if await engine.act("cancelReservation", ["id": reservation]) { await load(animated: true) }
    }

    // MARK: Week

    /// The room for the hours' names before the days; (1.3.170) the least a day of the week's grid is wide; an hour's
    /// least and most height (the least, or the hours' view's own share of the day, fits the whole day in it).
    private static let gutter: CGFloat = 46
    private static let dayLeast: CGFloat = 54
    private static let hourLeast: CGFloat = 18
    private static let hourMost: CGFloat = 140
    /// (1.3.170) The hours' view on a page that scrolls.
    private static let hoursTall: CGFloat = 520

    /// An hour's height as drawn: as pinched, and never less than the hours' view's share of the day — the whole day
    /// fills it at the least.
    private var hour: CGFloat {
        max(hourHeight, weekViewport > 0 ? (weekViewport - 2) / 24 : 0)
    }

    /// (1.3.147) The week as the Mac's Calendar draws one: its days side by side over the hours, each item at its time,
    /// the day-long ones over the hours — (1.3.170) its five days or seven, the whole day's hours scrolling under them;
    /// on a page too narrow for them, its days one under another.
    private func weekView(_ byDay: [String: [CalEvent]], size: CGSize?) -> some View {
        let days = (0..<weekDays).compactMap { cal.date(byAdding: .day, value: $0, to: weekStart) }
        let width = size?.width ?? room
        return ZStack(alignment: .top) {
            Group {
                if width >= weekLeast {
                    weekGrid(days, byDay: byDay, fills: size != nil)
                } else if size != nil {
                    ScrollView { dayCards(days, byDay: byDay) }
                } else {
                    dayCards(days, byDay: byDay)
                }
            }
            .padding(6) // (room for the cards' shadows inside the clip)
            .id(weekStart)
            .transition(slide)
        }
        .clipped()
        .padding(-6)
    }

    private func dayCards(_ days: [Date], byDay: [String: [CalEvent]]) -> some View {
        VStack(alignment: .leading, spacing: 18) {
            ForEach(days, id: \.self) { day in
                dayCard(day, events: byDay[key(day)] ?? [])
            }
        }
    }

    // MARK: To Do on the calendar

    /// The screenshot suite's -SimplTodoDrop "3,780": a row held over the week's fourth day at 1 PM, where it would go.
    private static var shotDrop: TodoDrop? {
        let parts = (UserDefaults.standard.string(forKey: "SimplTodoDrop") ?? "").split(separator: ",").compactMap { Int($0) }
        return parts.count == 2 ? TodoDrop(day: parts[0], minutes: parts[1]) : nil
    }

    /// Where a row dragged over a day would be planned: an hour's block at that time, its times on it. (1.3.172) Held
    /// over a plan, that plan's block, ringed: let go there, the row joins its group.
    private func todoDropBlock(_ d: TodoDrop, onto: WeekItem? = nil) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(onto == nil ? "\(clock(d.minutes)) – \(clock(d.minutes + 60))" : "Group with")
                .font(.system(size: 10.5, weight: .bold).monospacedDigit())
            Text(onto?.ev.title ?? (d.placed ? "Planning it…" : "Plan it here"))
                .font(.sCaption2)
                .lineLimit(2)
        }
        .foregroundStyle(Theme.accent)
        .padding(.horizontal, 6)
        .padding(.vertical, 4)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .frame(height: max(20, onto.map { CGFloat(length($0.ev) ?? 60) / 60 * hour } ?? hour) - 2, alignment: .top)
        .background(RoundedRectangle(cornerRadius: 8, style: .continuous).fill(Theme.accent.opacity(0.16)))
        .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(Theme.accent, style: StrokeStyle(lineWidth: onto == nil ? 1.5 : 2, dash: d.placed || onto != nil ? [] : [4, 3])))
        .padding(.horizontal, 3)
        .offset(y: weekY(onto?.at ?? d.minutes))
        .animation(reduceMotion ? nil : .easeOut(duration: 0.08), value: onto?.at ?? d.minutes)
        .allowsHitTesting(false)
        .accessibilityHidden(true)
    }

    /// A time of day, `minutes` into it, as the Mac writes one ("4:15 PM").
    private func clock(_ minutes: Int) -> String {
        let m = min(24 * 60 - 1, max(0, minutes))
        let at = cal.date(bySettingHour: m / 60, minute: m % 60, second: 0, of: Date()) ?? Date()
        return at.formatted(.dateTime.hour().minute())
    }

    /// A row let go on a day of the week: planned there for an hour (the page's planPut); the block stays where it was
    /// let go until the week is read again, and what came of it is said under To Do.
    private func planFromTodo(_ id: String, day: Int, minutes: Int, onto: WeekItem? = nil) {
        if let onto, let target = onto.ev.planId { // (1.3.172) let go over a plan: one block with it, the plan's group's
            todoDrop = nil
            NSHapticFeedbackManager.defaultPerformer.perform(.levelChange, performanceTime: .now)
            Task {
                do {
                    _ = try await engine.call("planJoin", ["id": id, "onto": target], as: ActReply.self)
                    say("Grouped with \(onto.ev.title)")
                    engine.changed()
                } catch {
                    let why = error.localizedDescription
                    withAnimation(reduceMotion ? nil : Motion.snappy) { failure = why }
                }
            }
            return
        }
        guard let date = cal.date(byAdding: .day, value: day, to: weekStart),
              let at = cal.date(bySettingHour: minutes / 60, minute: minutes % 60, second: 0, of: date) else {
            todoDrop = nil
            return
        }
        todoDrop = TodoDrop(day: day, minutes: minutes, placed: true)
        NSHapticFeedbackManager.defaultPerformer.perform(.levelChange, performanceTime: .now)
        Task {
            let r = try? await engine.call("planPut", ["id": id, "at": ISO8601DateFormatter().string(from: at), "mins": 60], as: ActReply.self)
            if r?.ok == true {
                say("Planned \(at.formatted(.dateTime.weekday(.wide).hour().minute()))")
                engine.changed()
            } else {
                withAnimation(reduceMotion ? nil : Motion.snappy) {
                    todoDrop = nil
                    failure = r?.error ?? "That time could not be planned."
                }
            }
        }
    }

    /// (1.3.170, the web's 2.99.112) A row let go on a day of the month or the agenda: planned at that day's first free
    /// time (the page's planDay), said under To Do.
    private func planOnDay(_ id: String, _ day: Date) {
        dayDrop = nil
        NSHapticFeedbackManager.defaultPerformer.perform(.levelChange, performanceTime: .now)
        Task {
            do {
                let r = try await engine.call("planDay", ["id": id, "day": ISO8601DateFormatter().string(from: cal.startOfDay(for: day))], as: PlanDayReply.self)
                say(r.words ?? "Planned")
                engine.changed()
            } catch {
                // (the page's own reason — "That day is already over" — as its answer's error says it)
                let why = error.localizedDescription
                withAnimation(reduceMotion ? nil : Motion.snappy) { failure = why }
            }
        }
    }

    /// (1.3.172) The screenshot suite's -SimplPlanPopover YES (in a home of its own: the plan is kept): the first piece
    /// of Canvas's work still open on To Do planned tomorrow (today, on the week's last day) at its first free time, as
    /// a row let go on that day is, then its block's popover opened — at its Edit with -SimplPlanEdit YES.
    private func shotPlan() async {
        struct Rows: Decodable {
            struct Section: Decodable { var rows: [Row] }
            struct Row: Decodable {
                var id: String
                var done: Bool?
                var custom: Bool?
            }
            var sections: [Section]
        }
        let today = cal.startOfDay(for: Date())
        let tomorrow = cal.date(byAdding: .day, value: 1, to: today) ?? today
        let last = cal.date(byAdding: .day, value: weekDays - 1, to: weekStart) ?? weekStart
        guard let rows = try? await engine.call("todo", ["panel": true], as: Rows.self),
              let first = rows.sections.flatMap(\.rows).first(where: { $0.done != true && $0.custom != true && $0.id.hasPrefix("assignment:") }) else { return }
        _ = try? await engine.call("planDay", ["id": first.id, "day": ISO8601DateFormatter().string(from: tomorrow > last ? today : tomorrow)], as: PlanDayReply.self)
        await load(animated: false)
        if let ev = data?.events.first(where: { $0.layer == "plan" && $0.planId == first.id }) { planOpen = ev.id }
    }

    /// (1.3.195) A plan's block: faded while held over To Do, gone once let go there.
    private func unplanFade(_ id: String) -> Double {
        if unplanned.contains(id) { return 0 }
        return unplanOver == id ? 0.45 : 1
    }

    /// (1.3.195) A plan's block let go over To Do: the plan (its group's too, as Delete Plan) taken off the calendar, so
    /// its work is back on To Do unplanned. The block fades where it is; should the page not take it, it comes back.
    private func unplan(_ ev: CalEvent, pid: String) {
        withAnimation(reduceMotion ? nil : .easeOut(duration: 0.18)) {
            unplanOver = nil
            joinOnto = nil
            planDrag = nil
            planFollow.clear()
            unplanned.insert(ev.id)
        }
        NSHapticFeedbackManager.defaultPerformer.perform(.levelChange, performanceTime: .now)
        Task {
            let r = try? await engine.call("planClear", ["id": pid, "group": true], as: ActReply.self)
            if r?.ok == true { say("Unplanned \(ev.title)") } else { say("Could not unplan \(ev.title)") }
            engine.changed()
            try? await Task.sleep(nanoseconds: 1_500_000_000)
            unplanned.remove(ev.id)
        }
    }

    /// What came of a row let go, under To Do for four seconds.
    private func say(_ words: String) {
        saidTicket += 1
        let ticket = saidTicket
        withAnimation(reduceMotion ? nil : Motion.snappy) { todoSaid = words }
        Task {
            try? await Task.sleep(nanoseconds: 4_000_000_000)
            guard ticket == saidTicket else { return }
            withAnimation(reduceMotion ? nil : Motion.snappy) { todoSaid = nil }
        }
    }

    /// How far down the week's hours a time of day is, `minutes` into the day.
    private func weekY(_ minutes: Int) -> CGFloat {
        CGFloat(Double(minutes) / 60) * hour
    }

    /// The minutes into its day an item is at; nil for one that lasts the day (or has no time).
    private func minutes(_ ev: CalEvent) -> Int? {
        guard ev.allDay != true, let t = ev.time, !t.isEmpty, let d = PreviewFormat.date(ev.date) else { return nil }
        let c = cal.dateComponents([.hour, .minute], from: d)
        return (c.hour ?? 0) * 60 + (c.minute ?? 0)
    }

    /// "8 AM", "Noon", "1 PM" — or "08", "13" on a Mac set to the 24-hour clock — as the Mac writes an hour.
    private func hourName(_ h: Int) -> String {
        guard let d = cal.date(bySettingHour: h, minute: 0, second: 0, of: Date()) else { return "\(h)" }
        let name = d.formatted(.dateTime.hour())
        return h == 12 && name.contains("PM") ? "Noon" : name
    }

    /// The week's head (each day's name and number), its day-long items, and the whole day's hours under them in a view
    /// of their own — `fills`: as tall as the page leaves it, else a fixed height on a page that scrolls.
    private func weekGrid(_ days: [Date], byDay: [String: [CalEvent]], fills: Bool) -> some View {
        let timed = days.map { day in
            (byDay[key(day)] ?? []).compactMap { ev in minutes(ev).map { WeekItem(ev: ev, at: $0) } }.sorted { $0.at < $1.at }
        }
        let allDay = days.map { day in (byDay[key(day)] ?? []).filter { minutes($0) == nil } }
        return VStack(spacing: 0) {
            HStack(spacing: 0) {
                Color.clear.frame(width: Self.gutter)
                ForEach(days, id: \.self) { day in
                    weekHeader(day, count: (byDay[key(day)] ?? []).count)
                        .frame(maxWidth: .infinity)
                }
            }
            .padding(.vertical, 4)
            .fixedSize(horizontal: false, vertical: true) // (1.3.170: as tall as the days' names, the hours given the rest)
            Self.line.frame(height: 1)
            if allDay.contains(where: { !$0.isEmpty }) {
                HStack(alignment: .top, spacing: 0) {
                    Text("all-day")
                        .font(.sCaption2)
                        .foregroundStyle(.secondary)
                        .frame(width: Self.gutter - 8, alignment: .trailing)
                        .padding(.trailing, 8)
                        .padding(.top, 7)
                    ForEach(Array(days.enumerated()), id: \.element) { i, _ in
                        VStack(spacing: 3) {
                            ForEach(allDay[i]) { weekBlock($0) }
                        }
                        .padding(3)
                        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
                        .overlay(alignment: .leading) { Self.line.frame(width: 1) }
                    }
                }
                .fixedSize(horizontal: false, vertical: true) // (every day's strip as tall as the fullest)
                Self.line.frame(height: 1)
            }
            weekHours(days, timed: timed)
                .frame(height: fills ? nil : Self.hoursTall)
                .frame(maxHeight: fills ? .infinity : nil)
        }
        .clipShape(RoundedRectangle(cornerRadius: 16, style: .continuous))
        .card()
    }

    /// (1.3.170, the web's 2.99.112) The whole day's hours, 12 AM to 11 PM, scrolled inside the week: opened at the
    /// morning (or a few hours before now, later today) and kept where they were left as the week is drawn again or moved;
    /// a pinch — or ⌘ or ⌃ and the wheel — shows more or less of the day, the time under the pointer kept still where it
    /// can be, and the hour's height kept. What lies above or below the hours in view waits squished at that edge of its
    /// day, a press scrolling to it.
    private func weekHours(_ days: [Date], timed: [[WeekItem]]) -> some View {
        let span = 24 * hour
        let today = days.contains(where: { cal.isDateInToday($0) })
        let peeks: [[WeekPeek]] = timed.map { items in items.map(peek) }
        return GeometryReader { g in
            ScrollView(.vertical) {
                HStack(alignment: .top, spacing: 0) {
                    VStack(spacing: 0) {
                        ForEach(0..<24, id: \.self) { h in
                            Text(hour >= 26 || h % 2 == 0 ? hourName(h) : "")
                                .font(.sCaption2.monospacedDigit())
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                                .minimumScaleFactor(0.8)
                                .padding(.trailing, 8)
                                .padding(.top, h == 0 ? 3 : 0)
                                .offset(y: h == 0 ? 0 : -7)
                                .frame(width: Self.gutter, height: hour, alignment: .topTrailing)
                        }
                    }
                    .overlay(alignment: .topTrailing) {
                        if today { nowTime() }
                    }
                    ForEach(Array(days.enumerated()), id: \.element) { i, day in
                        WeekDayStack {
                            ForEach(timed[i]) { item in
                                planDraggable(item, day: i, week: timed)
                                    .layoutValue(key: WeekAt.self, value: weekY(item.at))
                                    .layoutValue(key: WeekLength.self, value: drawnHeight(item.ev))
                                    .layoutValue(key: WeekLane.self, value: drawnHeight(item.ev, resting: true))
                                    .zIndex(planStretch?.id == item.ev.id || planDrag?.id == item.ev.id ? 1 : 0) // (a block pulled or dragged over the rest)
                            }
                        }
                        .padding(.horizontal, 3)
                        .frame(maxWidth: .infinity, minHeight: span, maxHeight: .infinity, alignment: .top)
                        .background(Theme.accent.opacity(cal.isDateInToday(day) ? 0.045 : 0))
                        .background { // (the day's width, for a dragged plan to say which day it is over)
                            GeometryReader { c in
                                Color.clear
                                    .onAppear { weekColumn = c.size.width }
                                    .onChange(of: c.size.width) { _, w in weekColumn = w }
                            }
                        }
                        .overlay(alignment: .top) {
                            if cal.isDateInToday(day) { nowLine() }
                        }
                        .overlay(alignment: .leading) { Self.line.frame(width: 1) }
                        .overlay(alignment: .top) { // (1.3.169) where a row of To Do dragged over this day would be planned
                            if let d = todoDrop, d.day == i { todoDropBlock(d, onto: d.placed ? nil : planAt(timed[i], minute: d.minutes + 30, not: nil)) }
                        }
                        .onDrop(of: [.text], delegate: TodoDropDelegate(day: i, hourHeight: hour, enabled: todoOn, scroller: weekScroll, drop: $todoDrop) { id, mins in
                            planFromTodo(id, day: i, minutes: mins, onto: planAt(timed[i], minute: mins + 30, not: nil))
                        })
                        .zIndex(timed[i].contains(where: { $0.ev.id == planDrag?.id }) ? 1 : 0) // (a dragged plan over the days beside)
                    }
                }
                .fixedSize(horizontal: false, vertical: true) // (every day as tall as the hours, or the fullest day)
                .coordinateSpace(name: Self.gridSpace) // (1.3.185: a dragged plan measured here, where nothing moves)
                .background(alignment: .top) {
                    // (a hairline at each hour, past the hours' names)
                    VStack(spacing: 0) {
                        ForEach(0..<24, id: \.self) { h in
                            VStack(spacing: 0) {
                                if h > 0 { Self.line.opacity(0.7).frame(height: 1) }
                                Spacer(minLength: 0)
                            }
                            .frame(height: hour)
                        }
                    }
                    .padding(.leading, Self.gutter)
                }
                .background { WeekScrollProbe(scroller: weekScroll) }
            }
            .scrollIndicators(.never) // (the hours' columns as wide as the days' names over them, on every Mac)
            .overlay {
                WeekPeeks(scroller: weekScroll, days: peeks, gutter: Self.gutter) { y in weekScroll.scroll(to: y - 28, animated: !reduceMotion) }
            }
            .simultaneousGesture(
                MagnifyGesture()
                    .onChanged { v in
                        let from = pinchFrom ?? hourHeight
                        if pinchFrom == nil { pinchFrom = from }
                        zoomHours(to: from * v.magnification, at: v.startLocation.y)
                    }
                    .onEnded { _ in
                        pinchFrom = nil
                        keepHour()
                    }
            )
            .onAppear {
                weekViewport = g.size.height
                weekScroll.opening = { openingY() }
                weekScroll.zoom = { factor, y in
                    zoomHours(to: hourHeight * factor, at: y)
                    keepHourSoon()
                }
            }
            .onChange(of: g.size.height) { _, h in weekViewport = h }
        }
    }

    /// (1.3.170) An item of the week down its hours, for what waits at the hours' edges: from its time, as long as it
    /// lasts (or as tall as a block is at least).
    private func peek(_ item: WeekItem) -> WeekPeek {
        let start = weekY(item.at)
        return WeekPeek(id: item.ev.id, start: start, end: start + drawnHeight(item.ev), color: Color(hex: item.ev.color), title: item.ev.title)
    }

    /// (1.3.170) Where the hours open, from their top: 7:30 in the morning — or, with today in the week and the afternoon
    /// begun, three hours before now (the screenshot suite's -SimplCalScrollHour 12: noon).
    private func openingY() -> CGFloat {
        let asked = UserDefaults.standard.double(forKey: "SimplCalScrollHour")
        if asked > 0 { return CGFloat(asked) * hour }
        let now = Date()
        let shown = (0..<weekDays).contains { cal.isDate(cal.date(byAdding: .day, value: $0, to: weekStart) ?? weekStart, inSameDayAs: now) }
        let h = cal.component(.hour, from: now)
        return CGFloat(shown && h >= 13 ? Double(h - 3) : 7.5) * hour
    }

    /// The hours shown larger or smaller, `to` an hour's height (between the least, or the day filling the view, and the
    /// most), the time `at` the pointer (from the view's top) kept under it.
    private func zoomHours(to wanted: CGFloat, at y: CGFloat) {
        let least = max(Self.hourLeast, weekScroll.height > 0 ? (weekScroll.height - 2) / 24 : Self.hourLeast)
        let next = min(Self.hourMost, max(least, wanted))
        guard abs(next - hour) > 0.05 else { return }
        let hours = (weekScroll.top + y) / hour
        weekScroll.scrollAfterLayout(to: hours * next - y)
        var t = Transaction()
        t.disablesAnimations = true
        withTransaction(t) { hourHeight = next }
    }

    /// (1.3.186) Calendars' popover closed, then Sync opened (its accounts and links are added and taken away there).
    private func openSync() {
        choosing = false
        Task {
            try? await Task.sleep(nanoseconds: 200_000_000)
            syncing = true
        }
    }

    /// (1.3.186) The calendars shown that are not Canvas's: each account's turned on, and those linked by their address
    /// turned on (the page's cal:links).
    private static func linkedShownNow() -> Int {
        let fromAccounts = CalendarAccounts.shared.accounts.filter { !$0.signedOut }.reduce(0) { $0 + $1.calendars.filter(\.on).count }
        let links = Bridge.shared.stored(["cal:links"])["cal:links"] as? [[String: Any]] ?? []
        let byAddress = links.filter { ($0["source"] as? String) != "account" && ($0["on"] as? Bool) != false }.count // (on unless turned off, as calSync says)
        return fromAccounts + byAddress
    }

    /// The hour's height kept for next time (a pinch let go; the wheel at rest a moment).
    private func keepHour() {
        UserDefaults.standard.set(Double(hourHeight), forKey: Self.hourKey)
    }

    private func keepHourSoon() {
        hourTicket += 1
        let ticket = hourTicket
        Task {
            try? await Task.sleep(nanoseconds: 350_000_000)
            if ticket == hourTicket { keepHour() }
        }
    }

    /// Now, on today's column: a red line at the minute, kept there as the minutes go by.
    private func nowLine() -> some View {
        TimelineView(.everyMinute) { context in
            let c = cal.dateComponents([.hour, .minute], from: context.date)
            let at: CGFloat = weekY((c.hour ?? 0) * 60 + (c.minute ?? 0))
            HStack(spacing: 0) {
                Circle().fill(Color.red).frame(width: 7, height: 7)
                Rectangle().fill(Color.red).frame(height: 1.5)
            }
            .offset(x: -3.5, y: at - 3.5)
            .allowsHitTesting(false)
            .accessibilityHidden(true)
        }
    }

    /// (1.3.167) The time it is now, in red beside the hours at the red line (as Calendar shows it), over the hour it
    /// falls near.
    private func nowTime() -> some View {
        TimelineView(.everyMinute) { context in
            let c = cal.dateComponents([.hour, .minute], from: context.date)
            let at: CGFloat = weekY((c.hour ?? 0) * 60 + (c.minute ?? 0))
            Text(context.date.formatted(.dateTime.hour(.defaultDigits(amPM: .omitted)).minute()))
                .font(.system(size: 10.5, weight: .bold).monospacedDigit())
                .foregroundStyle(Color.red)
                .padding(.horizontal, 3)
                .padding(.vertical, 1)
                .background(RoundedRectangle(cornerRadius: 4, style: .continuous).fill(Theme.card))
                .padding(.trailing, 5)
                .offset(y: at - 8)
                .allowsHitTesting(false)
                .accessibilityHidden(true)
        }
    }

    /// (1.3.176) How tall an item's block is on the week: as long as it lasts — a plan, a linked calendar's event, (1.3.180)
    /// an event of Canvas's own, a class, from its start to its end — and a due time (a moment, which has no end) half an
    /// hour; an event that says no end an hour (as Windows draws one). Never taller for its name (cut short to fit) and
    /// never too short to read a line.
    /// (1.3.181) `resting`: as long as it is, a foot being pulled or not — the room it keeps among the day's other items.
    private func drawnHeight(_ ev: CalEvent, resting: Bool = false) -> CGFloat {
        let unsaid = ["Event", "Appointment"].contains(ev.kind ?? "") ? 60 : Self.momentMinutes
        return max(Self.blockLeast, CGFloat((resting ? length(ev) : shownLength(ev)) ?? unsaid) / 60 * hour - 2)
    }
    /// (1.3.176) A due time's block, in minutes; the least a block is, in points (a line of its name).
    private static let momentMinutes = 30
    /// (1.3.176) A dragged block settling into its place: quick, no bounce.
    private static let settle = Animation.spring(response: 0.28, dampingFraction: 1)
    private static let blockLeast: CGFloat = 18
    /// (1.3.185) The week's grid of hours, a dragged plan's block measured on it.
    private static let gridSpace = "weekGrid"

    /// How long an item lasts, in minutes, when it says (a plan, a linked calendar's event): its block that tall.
    private func length(_ ev: CalEvent) -> Int? {
        guard let e = ev.end, let end = PreviewFormat.date(e), let start = PreviewFormat.date(ev.date) else { return nil }
        let m = Int(end.timeIntervalSince(start) / 60)
        return m > 0 ? max(m, 20) : nil
    }

    /// (1.3.167) A plan's block, dragged to move it: (1.3.176) it follows the pointer to the point, lifted, the quarter hour
    /// it would go to outlined under it with the time on a tag — and across to another day; let go, it settles into that
    /// quarter hour and the plan is moved there (the page's planMove;
    /// 1.3.172: one of a repeating plan's times moves the series by as much). A click without a drag opens its popover.
    /// (1.3.172, the web's 2.99.113 and 2.99.114) Its foot pulled makes it longer or shorter; held over another plan, that
    /// plan is ringed and the tag says "Group with …" — let go there, it joins that plan's group (its own group with it).
    /// Anything else on the grid is as it was.
    /// (1.3.185) Very smooth: the drag is measured on the week's grid, which stays still (measured on the block, which
    /// follows the pointer, each move undid the one before and the block flickered between its place and the finger);
    /// the block follows the pointer through PlanFollow, so only it is drawn again as it moves, the week and To Do only as
    /// the quarter hour or the day it would go to changes; it rides over the day's other items; and the trackpad ticks
    /// only as it crosses into another day, not every quarter hour.
    @ViewBuilder
    private func planDraggable(_ item: WeekItem, day: Int, week: [[WeekItem]]) -> some View {
        if item.ev.layer == "plan", let pid = item.ev.planId {
            let d = planDrag?.id == item.ev.id ? planDrag : nil
            let len = length(item.ev) ?? 60
            let onto = d.flatMap { d in d.dropped ? nil : planAt(dayOf(week, day + d.days), minute: item.at + d.minutes + d.grab, not: pid) }
            let held = d != nil && d?.dropped == false
            let settled = d.flatMap { d in d.dropped ? CGSize(width: CGFloat(d.days) * weekColumn, height: CGFloat(d.minutes) / 60 * hour) : nil }
            PlanFollowing(follow: planFollow, id: item.ev.id, settled: settled) {
                ZStack(alignment: .bottom) {
                    weekBlock(item.ev, fill: true)
                        .highPriorityGesture(
                            DragGesture(minimumDistance: 4, coordinateSpace: .named(Self.gridSpace))
                                .onChanged { g in
                                    // (1.3.176) the block to the point under the pointer, kept to the week and the day's
                                    // hours; the quarter hour and the day it would go to, from that
                                    let lo = -item.at, hi = max(lo, 24 * 60 - len - item.at)
                                    let dy = min(CGFloat(hi) / 60 * hour, max(CGFloat(lo) / 60 * hour, g.translation.height))
                                    let dx = weekColumn > 0 ? min(CGFloat(weekDays - 1 - day) * weekColumn, max(-CGFloat(day) * weekColumn, g.translation.width)) : 0
                                    let mins = min(hi, max(lo, Int((dy / hour * 60 / 15).rounded()) * 15))
                                    let days = weekColumn > 0 ? Int((dx / weekColumn).rounded()) : 0
                                    var t = Transaction()
                                    t.disablesAnimations = true
                                    withTransaction(t) { planFollow.move(item.ev.id, to: CGSize(width: dx, height: dy)) }
                                    // (1.3.195) held out past the week's left edge, over To Do beside it: let go there,
                                    // it is unplanned
                                    let leaving = todoBeside && todoOn && mode != "list" && g.location.x < -Self.todoGap / 2
                                    if leaving != (unplanOver == item.ev.id) {
                                        withAnimation(reduceMotion ? nil : .easeOut(duration: 0.12)) { unplanOver = leaving ? item.ev.id : nil }
                                        if leaving { joinOnto = nil }
                                        NSHapticFeedbackManager.defaultPerformer.perform(.alignment, performanceTime: .now)
                                    }
                                    let was = planDrag?.id == item.ev.id ? planDrag : nil
                                    guard was == nil || was?.days != days || was?.minutes != mins else { return }
                                    // (how far into the block it was taken: where it starts on the grid, less the block's top)
                                    let grab = was?.grab ?? max(0, Int((g.startLocation.y - weekY(item.at)) / hour * 60))
                                    withTransaction(t) { planDrag = PlanDrag(id: item.ev.id, days: days, minutes: mins, grab: grab) }
                                    let over = leaving ? nil : planAt(dayOf(week, day + days), minute: item.at + mins + grab, not: pid)?.ev.id
                                    if joinOnto != over { joinOnto = over }
                                    if let was, was.days != days { NSHapticFeedbackManager.defaultPerformer.perform(.alignment, performanceTime: .now) }
                                }
                                .onEnded { _ in
                                    if unplanOver == item.ev.id { // (1.3.195) let go over To Do: no longer planned
                                        unplan(item.ev, pid: pid)
                                        return
                                    }
                                    guard let d = planDrag, d.id == item.ev.id else {
                                        planFollow.clear()
                                        return
                                    }
                                    joinOnto = nil
                                    if let target = planAt(dayOf(week, day + d.days), minute: item.at + d.minutes + d.grab, not: pid), let onto = target.ev.planId {
                                        withAnimation(reduceMotion ? nil : Self.settle) {
                                            planDrag?.dropped = true
                                            planFollow.clear()
                                        }
                                        Task {
                                            let r = try? await engine.call("planJoin", ["id": pid, "onto": onto, "whole": true], as: ActReply.self)
                                            if r?.ok == true { say("Grouped with \(target.ev.title)") } else { withAnimation(Motion.snappy) { planDrag = nil } }
                                            engine.changed()
                                        }
                                        return
                                    }
                                    guard d.days != 0 || d.minutes != 0, let start = PreviewFormat.date(item.ev.date),
                                          let moved = cal.date(byAdding: .minute, value: d.days * 24 * 60 + d.minutes, to: start) else {
                                        withAnimation(reduceMotion ? nil : Self.settle) { // (back where it was)
                                            planDrag = nil
                                            planFollow.clear()
                                        }
                                        return
                                    }
                                    withAnimation(reduceMotion ? nil : Self.settle) { // (into its quarter hour)
                                        planDrag?.dropped = true
                                        planFollow.clear()
                                    }
                                    var args: [String: Any] = ["id": pid, "at": ISO8601DateFormatter().string(from: moved)]
                                    if let occurs = item.ev.occurs { args["occurs"] = occurs }
                                    Task {
                                        let r = try? await engine.call("planMove", args, as: ActReply.self)
                                        if r?.ok != true { withAnimation(Motion.snappy) { planDrag = nil } }
                                        engine.changed()
                                    }
                                }
                        )
                    stretchGrip(item, pid: pid, len: len)
                }
                .overlay {
                    if joinOnto == item.ev.id { // (another plan held over this one: let go, they are one block)
                        RoundedRectangle(cornerRadius: 8, style: .continuous)
                            .strokeBorder(Theme.accent, lineWidth: 2)
                            .allowsHitTesting(false)
                    }
                }
                .overlay(alignment: .bottom) {
                    if let s = planStretch, s.id == item.ev.id, !s.dropped {
                        Text("\(clock(item.at)) – \(clock(item.at + s.mins))")
                            .font(.system(size: 10.5, weight: .semibold).monospacedDigit())
                            .foregroundStyle(.white)
                            .padding(.horizontal, 7)
                            .padding(.vertical, 2)
                            .background(Capsule().fill(Theme.accent))
                            .fixedSize()
                            .offset(y: 12)
                            .allowsHitTesting(false)
                    }
                }
                .scaleEffect(held && !reduceMotion ? 1.02 : 1)
                .shadow(color: .black.opacity(held ? 0.22 : 0), radius: 10, y: 5)
                .opacity(unplanFade(item.ev.id))
            }
            .background {
                // (1.3.176) where it would go: its quarter hour and its day, outlined, while it is held elsewhere
                if let d, !d.dropped, onto == nil {
                    RoundedRectangle(cornerRadius: 8, style: .continuous)
                        .fill(Theme.accent.opacity(0.1))
                        .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous).strokeBorder(Theme.accent.opacity(0.7), style: StrokeStyle(lineWidth: 1.5, dash: [4, 3])))
                        .offset(x: CGFloat(d.days) * weekColumn, y: CGFloat(d.minutes) / 60 * hour)
                        .allowsHitTesting(false)
                }
            }
            .overlay(alignment: .top) {
                if let d, !d.dropped {
                    Group {
                        if let onto { joinTag(onto.ev.title) } else { dragTime(item.at + d.minutes, day: day + d.days) }
                    }
                    .offset(x: CGFloat(d.days) * weekColumn, y: CGFloat(d.minutes) / 60 * hour - 12)
                }
            }
            .accessibilityHint("Drag to move the plan, or onto another plan to group them; drag its foot to make it longer or shorter")
        } else {
            weekBlock(item.ev, fill: true)
        }
    }

    /// (1.3.172) The plan of a day of the week whose block holds a minute of it (other than the plan `not`): what a dragged
    /// block, or a row of To Do, let go there joins.
    private func planAt(_ items: [WeekItem], minute: Int, not: String?) -> WeekItem? {
        items.first { it in
            guard it.ev.layer == "plan", it.ev.allDay != true, let p = it.ev.planId, p != not else { return false }
            return minute >= it.at && minute < it.at + (length(it.ev) ?? 60)
        }
    }

    /// The items of a day of the week (none past its ends).
    private func dayOf(_ week: [[WeekItem]], _ day: Int) -> [WeekItem] {
        week.indices.contains(day) ? week[day] : []
    }

    /// (1.3.172) An item's length as drawn: a plan's whose foot is being pulled, as long as it is pulled.
    private func shownLength(_ ev: CalEvent) -> Int? {
        if let s = planStretch, s.id == ev.id { return s.mins }
        return length(ev)
    }

    /// (1.3.172, the web's 2.99.113) A plan's foot: under the pointer a short bar shows on it and the pointer turns to
    /// the up-and-down arrow; pulled, the block follows a quarter of an hour at a time (its times on a tag under it), a
    /// quarter of an hour long at least and over by midnight; let go, the plan is that long (the page's planEdit), its
    /// group's every task with it.
    /// (1.3.181) Pulled steadily: the pull is measured in the window, not on the foot itself — the foot moves with the
    /// block's end, so measured there each quarter hour taken undid itself and the block flickered between two lengths;
    /// the up-and-down pointer is kept for the whole pull (it no longer flips back each time the pointer is a little
    /// past the foot); and the day's other items stay where they are until it is let go (WeekLane).
    private func stretchGrip(_ item: WeekItem, pid: String, len: Int) -> some View {
        let shown = gripHover == item.ev.id || planStretch?.id == item.ev.id
        return Capsule()
            .fill(Color.white.opacity(0.9))
            .frame(width: 22, height: 3)
            .shadow(color: .black.opacity(0.25), radius: 1)
            .opacity(shown ? 1 : 0)
            .frame(maxWidth: .infinity)
            .frame(height: 9)
            .contentShape(Rectangle())
            .onHover { inside in
                if inside {
                    gripHover = item.ev.id
                    if !stretchCursor { NSCursor.resizeUpDown.push(); stretchCursor = true }
                } else {
                    if gripHover == item.ev.id { gripHover = nil }
                    if stretchCursor, planStretch?.id != item.ev.id || planStretch?.dropped == true { NSCursor.pop(); stretchCursor = false }
                }
            }
            .gesture(
                DragGesture(minimumDistance: 2, coordinateSpace: .global)
                    .onChanged { g in
                        let pulled = Double(len) + Double(g.translation.height / hour) * 60
                        let mins = max(15, min(24 * 60 - item.at, Int((pulled / 15).rounded()) * 15))
                        if !stretchCursor { NSCursor.resizeUpDown.push(); stretchCursor = true }
                        let next = PlanStretch(id: item.ev.id, mins: mins)
                        guard next != planStretch else { return }
                        // (each quarter hour at once, as the pointer reaches it: nothing eased in after it)
                        var t = Transaction()
                        t.disablesAnimations = true
                        withTransaction(t) { planStretch = next }
                        NSHapticFeedbackManager.defaultPerformer.perform(.alignment, performanceTime: .now)
                    }
                    .onEnded { _ in
                        // (the pointer let go away from the foot: the up-and-down pointer goes with the pull)
                        if stretchCursor, gripHover != item.ev.id { NSCursor.pop(); stretchCursor = false }
                        guard let st = planStretch, st.id == item.ev.id else { return }
                        guard st.mins != len else {
                            planStretch = nil
                            return
                        }
                        planStretch?.dropped = true
                        Task {
                            let r = try? await engine.call("planEdit", ["id": pid, "mins": st.mins], as: ActReply.self)
                            if r?.ok != true { withAnimation(Motion.snappy) { planStretch = nil } }
                            engine.changed()
                        }
                    }
            )
            .accessibilityHidden(true)
    }

    /// (1.3.172) What a block dragged over another plan would do, on a tag over it.
    private func joinTag(_ title: String) -> some View {
        Text("Group with \(title)")
            .font(.system(size: 10.5, weight: .semibold))
            .lineLimit(1)
            .foregroundStyle(.white)
            .padding(.horizontal, 7)
            .padding(.vertical, 2)
            .background(Capsule().fill(Theme.accent))
            .frame(maxWidth: 220)
            .fixedSize()
            .allowsHitTesting(false)
    }

    /// The time a dragged plan would go to, on a tag over it.
    private func dragTime(_ minutes: Int, day: Int) -> some View {
        let at = cal.date(bySettingHour: min(23, max(0, minutes / 60)), minute: max(0, minutes % 60), second: 0, of: cal.date(byAdding: .day, value: day, to: weekStart) ?? weekStart) ?? weekStart
        return Text(at.formatted(.dateTime.weekday(.abbreviated).hour().minute()))
            .font(.system(size: 10.5, weight: .semibold).monospacedDigit())
            .foregroundStyle(.white)
            .padding(.horizontal, 7)
            .padding(.vertical, 2)
            .background(Capsule().fill(Theme.accent))
            .fixedSize()
            .allowsHitTesting(false)
    }

    /// A day's name and number over its column; a click picks the day (its month, when Month is chosen).
    private func weekHeader(_ day: Date, count: Int) -> some View {
        let isToday = cal.isDateInToday(day)
        let isSel = cal.isDate(day, inSameDayAs: selected)
        return Button {
            pick(day)
        } label: {
            VStack(spacing: 2) {
                Text(day.formatted(.dateTime.weekday(.abbreviated)).uppercased())
                    .font(.sCaption.weight(.semibold))
                    .tracking(0.6)
                    .foregroundStyle(.secondary)
                Text("\(cal.component(.day, from: day))")
                    .font(.sTitle3.weight(isToday || isSel ? .bold : .medium).monospacedDigit())
                    .foregroundStyle(isToday ? Color.white : (isSel ? Theme.accent : Color.primary))
                    .frame(width: 32, height: 32)
                    .background {
                        if isToday {
                            Circle().fill(Theme.accent)
                        } else if isSel {
                            Circle().fill(Theme.accent.opacity(0.16))
                        }
                    }
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 4)
            .animation(Motion.snappy, value: isSel)
        }
        .buttonStyle(RowButtonStyle(radius: 9))
        .padding(.horizontal, 2)
        .accessibilityLabel(Self.spoken(day, count: count))
        .accessibilityAddTraits(isSel ? .isSelected : [])
    }

    /// `fill`: as tall as the grid makes it (an item that says how long it lasts), rather than as its name needs.
    private func weekBlock(_ ev: CalEvent, fill: Bool = false) -> some View {
        withEventMenu(ev) {
            eventLink(ev, padded: false, radius: 8) {
                CalendarWeekBlock(event: ev, fill: fill)
            }
        }
        .help(ev.sub ?? ev.title)
    }

    /// (1.3.172, the web's 2.99.113) An item's own press: a plan's popover — when it is, what for, Edit and Delete Plan
    /// (PlanPopover) — and anything else's preview grown out of it.
    @ViewBuilder
    private func eventLink<V: View>(_ ev: CalEvent, padded: Bool = true, radius: CGFloat = 10, @ViewBuilder label: @escaping () -> V) -> some View {
        if ev.layer == "plan", ev.planId != nil {
            Button { planOpen = ev.id } label: { label().contentShape(Rectangle()) }
                .buttonStyle(.plain)
                .popover(isPresented: Binding(get: { planOpen == ev.id && !Self.shotInline }, set: { if !$0, planOpen == ev.id { planOpen = nil } }), arrowEdge: .trailing) {
                    PlanPopover(event: ev, startEditing: Self.shotPlanEdit) { planOpen = nil }
                        .environmentObject(engine)
                }
                .accessibilityHint("Shows the plan, to edit it")
        } else {
            PreviewLink(item: .event(ev, engine: engine), padded: padded, radius: radius, inline: false) { label() }
        }
    }

    // MARK: List

    /// The list's days — the next three weeks, or the days picked — each day with something on it as a section of its
    /// own. (1.3.170) On a page that holds still, the days scroll inside their column. (1.3.171) The month they are
    /// picked on is in To Do's column beside them (over them, on a page that scrolls).
    @ViewBuilder
    private func listView(_ byDay: [String: [CalEvent]], size: CGSize?) -> some View {
        if size != nil {
            ScrollView {
                listDays(byDay)
                    .padding(6)
            }
            .padding(-6)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        } else {
            listDays(byDay)
                .frame(maxWidth: .infinity, alignment: .leading)
        }
    }

    private func listDays(_ byDay: [String: [CalEvent]]) -> some View {
        let keys = byDay.keys.sorted()
        return VStack(alignment: .leading, spacing: 22) {
            if keys.isEmpty {
                EmptyNote(text: agenda.isStandard ? "Nothing in the next three weeks." : "Nothing in these days.", symbol: "calendar")
                    .padding(.horizontal, 18)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .card()
            }
            ForEach(keys, id: \.self) { k in
                if let day = Self.dayKey.date(from: k) {
                    dayCard(day, events: byDay[k] ?? [])
                        .transition(.opacity)
                }
            }
        }
    }

    private var agendaPicker: some View {
        AgendaPicker(span: agenda, from: agendaFrom, pick: pickAgenda, reset: resetAgenda)
    }

    /// (1.3.147) A day clicked in the list's picker: the first of a new span, then its last (one before the first makes
    /// that one the first) — at most 120 days, as the web's agenda has it. The list moves once both are picked.
    private func pickAgenda(_ day: Date) {
        focused = true
        guard let from = agendaFrom else {
            withAnimation(Motion.snappy) { agendaFrom = day }
            return
        }
        let start = min(from, day)
        var end = max(from, day)
        if let most = cal.date(byAdding: .day, value: 119, to: start), end > most { end = most }
        let next = AgendaSpan(start: start, end: end)
        Task {
            await move(start < agenda.start ? -1 : 1) {
                agendaFrom = nil
                agenda = next
            }
        }
        next.save()
    }

    /// Back to the next three weeks.
    private func resetAgenda() {
        focused = true
        let next = AgendaSpan.standard
        AgendaSpan.forget()
        guard next != agenda else {
            withAnimation(Motion.snappy) { agendaFrom = nil }
            return
        }
        Task {
            await move(next.start < agenda.start ? -1 : 1) {
                agendaFrom = nil
                agenda = next
            }
        }
    }

    /// (1.3.170) A day's "2 more" on a month with no day beside it: the day alone, in the Agenda (the web's).
    private func showDay(_ day: Date) {
        focused = true
        let d = cal.startOfDay(for: day)
        let span = AgendaSpan(start: d, end: d)
        span.save()
        withAnimation(Motion.gentle) {
            agendaFrom = nil
            agenda = span
            selected = d
            mode = "list"
        }
        Task {
            await engine.act("calView", ["view": "list"])
            await load(animated: true)
        }
    }

    // MARK: - Moving

    private func setMode(_ v: String) {
        guard v != mode else { return }
        focused = true
        withAnimation(Motion.gentle) {
            // (the week or the month of the day picked)
            if v == "week" { weekStart = Self.weekBegins(selected, days: weekDays) }
            if v == "month" { anchor = Self.monthStart(selected) }
            mode = v
        }
        Task {
            await engine.act("calView", ["view": v])
            await load(animated: true)
        }
    }

    private func step(_ way: Int) -> KeyPress.Result {
        shift(way)
        return .handled
    }

    /// A month or a week on or back; the day picked becomes today when it is in it, else its first day. (1.3.147) The
    /// list moves by as many days as it shows.
    private func shift(_ way: Int) {
        focused = true
        Task {
            await move(way) {
                let today = cal.startOfDay(for: Date())
                if mode == "list" {
                    let days = (cal.dateComponents([.day], from: agenda.start, to: agenda.last).day ?? 20) + 1
                    let start = cal.date(byAdding: .day, value: days * way, to: agenda.start) ?? agenda.start
                    agendaFrom = nil
                    agenda = AgendaSpan(start: start, end: cal.date(byAdding: .day, value: days - 1, to: start))
                    agenda.isStandard ? AgendaSpan.forget() : agenda.save()
                } else if mode == "week" {
                    let next = Self.weekBegins(cal.date(byAdding: .day, value: 7 * way, to: weekStart) ?? weekStart, days: weekDays)
                    let end = cal.date(byAdding: .day, value: weekDays, to: next) ?? next
                    weekStart = next
                    selected = today >= next && today < end ? today : next
                } else {
                    let next = cal.date(byAdding: .month, value: way, to: anchor) ?? anchor
                    anchor = next
                    selected = cal.isDate(today, equalTo: next, toGranularity: .month) ? today : next
                }
            }
        }
    }

    private func goToday() {
        focused = true
        if mode == "list" { return resetAgenda() }
        let today = cal.startOfDay(for: Date())
        let month = Self.monthStart(today)
        let week = Self.weekBegins(today, days: weekDays)
        let here = mode == "week" ? weekStart : anchor
        let there = mode == "week" ? week : month
        guard here != there else {
            withAnimation(Motion.snappy) { selected = today }
            return
        }
        Task {
            await move(there < here ? -1 : 1) {
                anchor = month
                weekStart = week
                selected = today
            }
        }
    }

    /// A move to another month or week, on the house spring, then read afresh. The way it goes is set a frame before the
    /// move itself, so the one leaving reads it as it leaves.
    private func move(_ way: Int, _ change: () -> Void) async {
        if dir != way {
            dir = way
            try? await Task.sleep(nanoseconds: 20_000_000)
        }
        withAnimation(Motion.gentle) { change() }
        await load(animated: true)
    }

    /// A day picked: one of the month before or after brings its month over, the way it lies.
    private func pick(_ day: Date) {
        focused = true
        let month = Self.monthStart(day)
        if mode == "month", month != anchor {
            Task {
                await move(month < anchor ? -1 : 1) {
                    anchor = month
                    selected = day
                }
            }
        } else {
            withAnimation(Motion.snappy) { selected = day }
        }
    }

    // MARK: - Reading

    /// The span shown: six weeks round the month, the week, or the list's days.
    private var range: (Date, Date) {
        Self.span(mode, anchor: anchor, weekStart: weekStart, days: weekDays, agenda: agenda)
    }

    private static func span(_ mode: String, anchor: Date, weekStart: Date, days: Int, agenda: AgendaSpan) -> (Date, Date) {
        let cal = Calendar.current
        switch mode {
        case "week":
            return (weekStart, cal.date(byAdding: .day, value: days, to: weekStart) ?? weekStart)
        case "list":
            return (agenda.start, cal.date(byAdding: .day, value: 1, to: agenda.last) ?? agenda.last)
        default:
            let s = Self.firstOfWeek(anchor)
            return (s, cal.date(byAdding: .day, value: 42, to: s) ?? s)
        }
    }

    /// The calendar's read for a span, as the page takes it.
    private static func args(_ span: (Date, Date)) -> [String: Any] {
        let iso = ISO8601DateFormatter()
        return ["from": iso.string(from: span.0), "to": iso.string(from: span.1)]
    }

    /// (1.3.78) What the Calendar opens on — its view as last chosen, the span round today — read ahead.
    static func readAhead() -> ReadAhead.Ask? {
        ReadAhead.Ask(key: "calendar|now") { engine in
            let v = (try? await engine.read("calView", as: OK.self, quiet: true))?.view ?? "month"
            let days = Self.keptDays
            let shown = Self.span(Self.modes.contains(v) ? v : "month", anchor: Self.monthStart(Date()), weekStart: Self.weekBegins(Date(), days: days),
                                  days: days, agenda: AgendaSpan.saved() ?? .standard)
            _ = try? await engine.read("calendar", Self.args(shown), as: CalendarData.self, quiet: true)
        }
    }

    /// (1.3.78) This session's answers at once (read ahead, or from a visit before): its view, and the span round today
    /// in it — whole on its first frame, asked again only once stale.
    private func seed() {
        guard data == nil else { return }
        if !modeRead, let v = engine.recent("calView", as: OK.self)?.value.view, Self.modes.contains(v) {
            mode = v
            modeRead = true
        }
        guard let held = engine.recent("calendar", Self.args(range), as: CalendarData.self) else { return }
        var t = Transaction()
        t.disablesAnimations = true
        withTransaction(t) { data = held.value }
        source = "cached"
    }

    private func load(animated: Bool) async {
        let (s, e) = range
        let ask = "\(mode)|\(Int(s.timeIntervalSince1970))|\(Int(e.timeIntervalSince1970))"
        asked = ask
        loading = true
        do {
            let d = try await engine.read("calendar", Self.args((s, e)), as: CalendarData.self)
            guard asked == ask else { return } // (a later move asked for another span)
            marks.shown("calendar", Self.args((s, e)), by: engine) // (1.3.80: the answer a live check asks after)
            // (1.3.185) a plan just let go, pulled or planned from To Do: put where the page has it at once — eased, its
            // block went back to where it was taken from before gliding to where it had been let go
            let settling = planDrag?.dropped == true || planStretch?.dropped == true || todoDrop?.placed == true
            if data == nil || !animated || settling {
                var t = Transaction()
                t.disablesAnimations = true
                withTransaction(t) {
                    data = d
                    failure = nil
                    if planDrag?.dropped == true { planDrag = nil } // (1.3.167: the plan dragged, now where the page has it)
                    if planStretch?.dropped == true { planStretch = nil } // (1.3.172: the plan pulled, now as long as the page has it)
                    if todoDrop?.placed == true { todoDrop = nil } // (1.3.169: the row let go, now a plan on the week)
                }
            } else {
                withAnimation(Motion.gentle) {
                    data = d
                    failure = nil
                }
                if planDrag?.dropped == true { planDrag = nil }
                if planStretch?.dropped == true { planStretch = nil }
                if todoDrop?.placed == true { todoDrop = nil }
            }
            error = nil
            linkedShown = Self.linkedShownNow()
            if !shotPlanned, UserDefaults.standard.bool(forKey: "SimplPlanPopover") {
                shotPlanned = true
                Task { await shotPlan() }
            }
        } catch {
            guard asked == ask else { return }
            if data == nil {
                self.error = error.localizedDescription
            } else {
                let why = error.localizedDescription
                withAnimation(Motion.gentle) { failure = why }
            }
        }
        loading = false
    }

    /// (1.3.80) The live check (LiveRefresh): the span shown asked again with the check's age; a new answer put in place
    /// — an event that came, a due date that moved — the rest left as it is. A move to another span made meanwhile is
    /// never undone by it.
    private func liveCheck(_ engine: Engine, _ age: Double) async throws -> LiveFound {
        let ask = asked
        let args = Self.args(range)
        guard let shown = data, !loading,
              let c = try await engine.liveCheck("calendar", args, as: CalendarData.self, shown: shown, marks: marks, maxAge: age) else {
            marks.at = Date()
            return .unchanged
        }
        return LiveFound(parts: ["calendar"]) {
            guard asked == ask else { return }
            c.put {
                withAnimation(LiveRefresh.motion) {
                    data = c.value
                    failure = nil
                }
            }
            marks.at = Date()
        }
    }
}

// MARK: - Pieces

/// (1.3.147) The list's days: its first and its last, both shown — the next three weeks from today until other days are
/// picked beside it (kept, as the web's agenda keeps its range).
struct AgendaSpan: Equatable {
    var start: Date
    var end: Date?

    /// The last day shown (three weeks' worth when no end was given).
    var last: Date { end ?? Calendar.current.date(byAdding: .day, value: 20, to: start) ?? start }

    static var standard: AgendaSpan {
        let s = Calendar.current.startOfDay(for: Date())
        return AgendaSpan(start: s, end: Calendar.current.date(byAdding: .day, value: 20, to: s))
    }

    var isStandard: Bool {
        let cal = Calendar.current
        let std = AgendaSpan.standard
        return cal.isDate(start, inSameDayAs: std.start) && cal.isDate(last, inSameDayAs: std.last)
    }

    private static let key = "SimplAgendaSpan"
    private static let dayKey: DateFormatter = {
        let f = DateFormatter()
        f.calendar = Calendar(identifier: .gregorian)
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()

    /// The days last picked, while they are not the next three weeks.
    static func saved() -> AgendaSpan? {
        guard let days = UserDefaults.standard.stringArray(forKey: key), days.count == 2,
              let s = dayKey.date(from: days[0]), let e = dayKey.date(from: days[1]), e >= s else { return nil }
        return AgendaSpan(start: s, end: e)
    }

    func save() {
        UserDefaults.standard.set([Self.dayKey.string(from: start), Self.dayKey.string(from: last)], forKey: Self.key)
    }

    static func forget() { UserDefaults.standard.removeObject(forKey: key) }
}

/// (1.3.147) The list's days picked on a small month beside it, as the web's agenda has them: a day clicked starts the
/// span, the next ends it; the days between washed in the accent, its ends on it. ‹ and › move the small month; Reset
/// goes back to the next three weeks.
private struct AgendaPicker: View {
    let span: AgendaSpan
    let from: Date?
    let pick: (Date) -> Void
    let reset: () -> Void
    /// The month shown when another than the span's first was moved to.
    @State private var month: Date?
    private var cal: Calendar { Calendar.current }

    private var shown: Date {
        let m = month ?? from ?? span.start
        return cal.date(from: cal.dateComponents([.year, .month], from: m)) ?? m
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(spacing: 6) {
                Text(shown.formatted(.dateTime.month(.wide).year()))
                    .font(.sHeadline)
                Spacer(minLength: 4)
                Button {
                    turn(-1)
                } label: {
                    Image(systemName: "chevron.left").frame(width: 14, height: 14)
                }
                .buttonStyle(.borderless)
                .help("Previous month")
                .accessibilityLabel("Previous Month")
                Button {
                    turn(1)
                } label: {
                    Image(systemName: "chevron.right").frame(width: 14, height: 14)
                }
                .buttonStyle(.borderless)
                .help("Next month")
                .accessibilityLabel("Next Month")
            }
            grid
            Divider()
            HStack(alignment: .center, spacing: 8) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(from.map { "\(Self.words($0)) – …" } ?? "\(Self.words(span.start)) – \(Self.words(span.last))")
                        .font(.sCallout.weight(.semibold).monospacedDigit())
                        .contentTransition(.numericText())
                    Text(from == nil ? "Click a day to start new days" : "Click the last day")
                        .font(.sFootnote)
                        .foregroundStyle(.secondary)
                }
                Spacer(minLength: 4)
                Button("Reset", action: reset)
                    .glassButton()
                    .disabled(from == nil && span.isStandard)
                    .help("Back to the next three weeks")
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .card()
        .onChange(of: span) { _, _ in month = nil }
        .animation(Motion.snappy, value: from)
        .animation(Motion.snappy, value: span)
    }

    private func turn(_ way: Int) {
        withAnimation(Motion.snappy) { month = cal.date(byAdding: .month, value: way, to: shown) }
    }

    private static func words(_ d: Date) -> String { d.formatted(.dateTime.month(.abbreviated).day()) }

    private var grid: some View {
        let back = (cal.component(.weekday, from: shown) - cal.firstWeekday + 7) % 7
        let first = cal.date(byAdding: .day, value: -back, to: shown) ?? shown
        let days = (0..<42).compactMap { cal.date(byAdding: .day, value: $0, to: first) }
        let symbols: [String] = {
            let s = cal.veryShortWeekdaySymbols
            guard s.count == 7 else { return s }
            let f = (cal.firstWeekday - 1 + 7) % 7
            return Array(s[f...] + s[..<f])
        }()
        return VStack(spacing: 2) {
            HStack(spacing: 0) {
                ForEach(Array(symbols.enumerated()), id: \.offset) { _, s in
                    Text(s)
                        .font(.sCaption2.weight(.semibold))
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity)
                }
            }
            .accessibilityHidden(true)
            ForEach(0..<6, id: \.self) { w in
                HStack(spacing: 0) {
                    ForEach(days[(w * 7)..<(w * 7 + 7)], id: \.self) { day in
                        cell(day)
                    }
                }
            }
        }
    }

    private func cell(_ day: Date) -> some View {
        let start = from ?? span.start
        let last = from ?? span.last
        let isStart = cal.isDate(day, inSameDayAs: start)
        let isEnd = cal.isDate(day, inSameDayAs: last)
        let inside = day > start && day < last
        let off = !cal.isDate(day, equalTo: shown, toGranularity: .month)
        let today = cal.isDateInToday(day)
        return Button {
            pick(cal.startOfDay(for: day))
        } label: {
            Text("\(cal.component(.day, from: day))")
                .font(.sCallout.weight(isStart || isEnd || today ? .semibold : .regular).monospacedDigit())
                .foregroundStyle(isStart || isEnd ? Color.white : (today ? Theme.accent : (off ? Color.secondary.opacity(0.6) : Color.primary)))
                .frame(width: 28, height: 28)
                .background {
                    if isStart || isEnd { Circle().fill(Theme.accent) }
                }
                .frame(maxWidth: .infinity)
                .frame(height: 30)
                // (the days between, and the half of each end toward them, washed in the accent: one band)
                .background {
                    HStack(spacing: 0) {
                        Theme.accent.opacity(inside || (isEnd && !isStart) ? 0.16 : 0)
                        Theme.accent.opacity(inside || (isStart && !isEnd) ? 0.16 : 0)
                    }
                    .padding(.vertical, 1)
                }
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel(day.formatted(.dateTime.weekday(.wide).month(.wide).day()))
        .accessibilityAddTraits(isStart || isEnd ? .isSelected : [])
    }
}

private extension PreviewItem {
    /// (1.2.3) A piece of work or an event of the calendar, for its preview: its line under the title is "course · kind ·
    /// points · place" (native-app.js calendar). An event whose address is the calendar itself opens on Canvas's page.
    @MainActor
    static func event(_ ev: CalEvent, engine: Engine) -> PreviewItem {
        let kind = PreviewFormat.text(ev.kind) ?? "Event"
        let work = ["Assignment", "Quiz", "Discussion"].contains(kind)
        let parts = (ev.sub ?? "").components(separatedBy: " · ").map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
        let course = parts.first.flatMap { c -> String? in c == kind || c.hasSuffix(" pts") ? nil : c }
        let place = parts.filter { $0 != course && $0 != kind && !$0.hasSuffix(" pts") && !$0.hasSuffix(" pt") }
        var item = PreviewItem(id: "cal:\(ev.id)", title: ev.title, kind: kind, symbol: CalendarEventLine.glyph(kind), course: course, color: ev.color)
        let day = PreviewFormat.date(ev.date).map { PreviewFormat.dayWord($0) }
        item.when = PreviewFormat.when(day: day, time: ev.time, due: work)
        item.points = PreviewFormat.points(in: ev.sub)
        item.detail = place.isEmpty ? nil : place.joined(separator: " · ")
        if ev.missing == true {
            item.flags = [WorkFlag(word: "Missing", kind: "bad")]
        } else if ev.excused == true {
            item.flags = [WorkFlag(word: "Excused", kind: "muted")]
        } else if work && ev.done == true {
            item.flags = [WorkFlag(word: "Submitted", kind: "good")]
        }
        if let url = PreviewFormat.text(ev.url) {
            item.url = url
            if engine.tabName(for: url) == "calendar" {
                // (Canvas's own page for an event: here the calendar would only open on itself)
                item.openLabel = "Open in \(engine.lmsName)"
                item.openWhole = { engine.openWebScreen(url, title: ev.title) }
            } else {
                item.openWhole = { engine.openWeb(url, title: ev.title) }
            }
            if work {
                item.work = WorkAction(url: url, title: ev.title, kind: kind.lowercased(), handedIn: ev.done == true && ev.missing != true)
            }
        }
        return item
    }
}

extension CalEvent {
    /// (planning-24) Struck through: work handed in or excused, in every view (the engine's `struck`; before 2.99.33,
    /// work handed in) — never a lecture or a lab that is only over.
    var isStruck: Bool {
        if let struck { return struck }
        return missing != true && (excused == true || (done == true && ["Assignment", "Quiz", "Discussion"].contains(kind ?? "")))
    }

    /// Greyed: over (its due time or its end gone by), or struck.
    var isPast: Bool { (past ?? (done == true)) || isStruck }
}

/// A piece of work or an event on a day of the month: (1.2) a line on the grid, (planning-13) a thin bar in its
/// calendar's colour before its name — a red mark when it is missing; greyed once over, struck through only once
/// handed in. A name may take `lines` lines.
private struct CalendarChip: View {
    let event: CalEvent
    var lines = 1

    var body: some View {
        let c = Color(hex: event.color)
        let missing = event.missing == true
        let grey = event.isPast && !missing
        HStack(alignment: .firstTextBaseline, spacing: 4) {
            if missing {
                Image(systemName: "exclamationmark.circle.fill")
                    .font(.sCaption2.weight(.bold))
                    .foregroundStyle(.red)
                    .frame(width: 8)
            }
            if event.layer == "plan" { // (1.3.165) a time set aside for a piece of work: its clock before its name
                Image(systemName: "clock")
                    .font(.sCaption2.weight(.semibold))
                    .foregroundStyle(c)
                    .padding(.leading, 7)
            }
            // (a name on two lines only where each of its words fits a line: a word wider than the day — "Composition" —
            // was broken in the middle, "Compositio / n"; such a name keeps to one line, cut at its end)
            ViewThatFits(in: .horizontal) {
                if lines > 1 {
                    title(lines: lines)
                        .frame(idealWidth: CalendarChip.longestWord(event.title), alignment: .leading)
                }
                title(lines: 1)
            }
            .foregroundStyle(grey || event.layer == "linked" ? Color.secondary : (missing ? Color.red : Color.primary))
            .padding(.leading, missing || event.layer == "plan" ? 0 : 7)
                .overlay(alignment: .leading) {
                    if !missing && event.layer != "plan" {
                        Capsule()
                            .fill(c.opacity(grey ? 0.55 : 1))
                            .frame(width: 3)
                            .padding(.vertical, 1.5)
                    }
                }
        }
        .font(.sCaption)
        .padding(.horizontal, 4)
        .padding(.vertical, 1)
        .frame(maxWidth: .infinity, alignment: .leading)
        .accessibilityHidden(true)
    }

    private func title(lines: Int) -> some View {
        Text(event.title)
            .strikethrough(event.isStruck && event.missing != true, color: .secondary)
            .lineLimit(lines)
            .fixedSize(horizontal: false, vertical: true)
    }

    /// The width of a name's widest word at the chip's size (12 points, as `.sCaption`), and a point to spare.
    static func longestWord(_ title: String) -> CGFloat {
        let font = NSFont.systemFont(ofSize: 12)
        let widest = title.split(whereSeparator: { $0 == " " || $0 == "\n" })
            .map { (String($0) as NSString).size(withAttributes: [.font: font]).width }
            .max() ?? 0
        return ceil(widest) + 1
    }
}

/// A piece of work or an event in a day's list: its kind's icon in its calendar's colour, its name (struck through once
/// done), its course, kind, points and place, whether it is missing or excused, and its time.
private struct CalendarEventLine: View {
    let event: CalEvent

    var body: some View {
        let missing = event.missing == true
        HStack(spacing: 12) {
            IconTile(symbol: CalendarEventLine.glyph(event.kind), color: Color(hex: event.color))
            VStack(alignment: .leading, spacing: 2) {
                // (planning-24) struck through only once handed in, greyed once over: as on the month's grid
                Text(event.title)
                    .font(.sBody)
                    .strikethrough(event.isStruck && !missing, color: .secondary)
                    .foregroundStyle(event.isPast && !missing ? Color.secondary : Color.primary)
                    .lineLimit(2)
                if let sub = event.sub, !sub.isEmpty {
                    Text(sub).font(.sCallout).foregroundStyle(.secondary).lineLimit(1)
                }
            }
            Spacer(minLength: 6)
            if missing {
                FlagBadge(flag: WorkFlag(word: "Missing", kind: "bad"))
            } else if event.excused == true {
                FlagBadge(flag: WorkFlag(word: "Excused", kind: "muted"))
            }
            if let time = event.time, !time.isEmpty {
                Text(time)
                    .font(.sCallout.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .frame(minWidth: 58, alignment: .trailing)
            }
        }
        .contentShape(Rectangle())
    }

    static func glyph(_ kind: String?) -> String {
        switch kind ?? "" {
        case "Assignment": return "doc.text"
        case "Quiz": return "questionmark.square" // (courses-8: a quiz's own symbol everywhere; the checklist is To Do's)
        case "Discussion": return "bubble.left.and.bubble.right"
        case "Appointment": return "person.2"
        case "Plan": return "clock" // (1.3.165)
        default: return "calendar"
        }
    }
}

/// A piece of work or an event in a day of the week: (1.3.147) a small block washed in its calendar's colour, a bar of it
/// before its name, its time under it (or that it is missing: then in red, a mark before the time); greyed once over,
/// struck through only once handed in. The names wrap to three lines in a narrow day.
private struct CalendarWeekBlock: View {
    let event: CalEvent
    var fill = false

    var body: some View {
        let c = Color(hex: event.color)
        let missing = event.missing == true
        let grey = event.isPast && !missing
        let time = event.allDay == true ? nil : PreviewFormat.text(event.time)
        // (1.3.176) as much as its height holds: its name on up to three lines and its time under it; one line of name
        // and its time; or, in the shortest, its name and its time on one line — never spilling out of it
        ViewThatFits(in: .vertical) {
            VStack(alignment: .leading, spacing: 1) {
                name(lines: 3)
                timeLine(time: time, missing: missing)
            }
            VStack(alignment: .leading, spacing: 1) {
                name(lines: 1)
                timeLine(time: time, missing: missing)
            }
            HStack(alignment: .firstTextBaseline, spacing: 4) {
                name(lines: 1)
                if let time {
                    Text(time)
                        .font(.sCaption2.monospacedDigit())
                        .foregroundStyle(Color.secondary)
                        .lineLimit(1)
                        .layoutPriority(-1)
                }
            }
        }
        .padding(.leading, 8)
        .padding(.trailing, 4)
        .padding(.vertical, fill ? 2 : 4)
        .frame(maxWidth: .infinity, maxHeight: fill ? .infinity : nil, alignment: .topLeading) // (1.3.167: as tall as it lasts)
        .clipped()
        .background {
            RoundedRectangle(cornerRadius: 6, style: .continuous)
                .fill(c.opacity(grey ? 0.07 : 0.15))
        }
        .overlay(alignment: .leading) {
            Capsule()
                .fill(c.opacity(grey ? 0.5 : 1))
                .frame(width: 3)
                .padding(.vertical, 3)
                .padding(.leading, 2)
        }
        .overlay { // (1.3.165) a plan: a dashed edge, a time set aside rather than something set
            if event.layer == "plan" {
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .strokeBorder(c.opacity(0.8), style: StrokeStyle(lineWidth: 1.2, dash: [4, 3]))
            }
        }
        .contentShape(Rectangle())
    }

    /// Its name, struck through once handed in, greyed once over (planning-24: as on the month's grid).
    private func name(lines: Int) -> some View {
        Text(event.title)
            .font(.sCaption.weight(.semibold))
            .strikethrough(event.isStruck && event.missing != true, color: .secondary)
            .foregroundStyle(event.isPast && event.missing != true ? Color.secondary : Color.primary)
            .lineLimit(lines)
            .multilineTextAlignment(.leading)
            .fixedSize(horizontal: false, vertical: true)
    }

    /// Its time — or Missing, or Excused — under its name.
    @ViewBuilder
    private func timeLine(time: String?, missing: Bool) -> some View {
        if time != nil || missing || event.excused == true {
            HStack(spacing: 3) {
                if missing {
                    Image(systemName: "exclamationmark.circle.fill")
                }
                Text(time ?? (missing ? "Missing" : "Excused"))
                    .monospacedDigit()
            }
            .font(.sCaption2)
            .foregroundStyle(missing ? Color.red : Color.secondary)
            .lineLimit(1)
        }
    }
}

/// (1.3.147) A day of the week's grid: each item at its time. (1.3.176) Each as tall as it lasts (WeekLength), never
/// taller for its name; items whose times meet side by side, sharing the day's width, as Calendar lays them — none
/// pushed down off its time. Kept inside the day's hours.
private struct WeekDayStack: Layout {
    var gap: CGFloat = 2

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width ?? 100
        return CGSize(width: width, height: frames(subviews, width: width).map(\.maxY).max() ?? 0)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        for (s, f) in zip(subviews, frames(subviews, width: bounds.width)) {
            s.place(at: CGPoint(x: bounds.minX + f.minX, y: bounds.minY + f.minY), proposal: ProposedViewSize(width: f.width, height: f.height))
        }
    }

    private func frames(_ subviews: Subviews, width: CGFloat) -> [CGRect] {
        // each item's time and length, in points, and (1.3.181) the length it keeps its column by: a plan's foot being
        // pulled moves nothing else of the day, and the block keeps its place, until it is let go
        let spans = subviews.map { s -> (y: CGFloat, h: CGFloat, lane: CGFloat) in
            let h = s[WeekLength.self] > 0 ? s[WeekLength.self] : s.sizeThatFits(ProposedViewSize(width: width, height: nil)).height
            return (s[WeekAt.self], h, s[WeekLane.self] > 0 ? s[WeekLane.self] : h)
        }
        // the items in runs whose times meet, each run's items in the fewest columns (the first that is free by then)
        var column = [Int](repeating: 0, count: spans.count)
        var columns = [Int](repeating: 1, count: spans.count)
        var run: [Int] = [], ends: [CGFloat] = [], runEnd: CGFloat = -.greatestFiniteMagnitude
        func close() { for k in run { columns[k] = max(1, ends.count) } }
        for (k, sp) in spans.enumerated() {
            if sp.y >= runEnd - 0.5 {
                close()
                run = []
                ends = []
            }
            if let free = ends.firstIndex(where: { $0 <= sp.y + 0.5 }) {
                column[k] = free
                ends[free] = sp.y + sp.lane
            } else {
                column[k] = ends.count
                ends.append(sp.y + sp.lane)
            }
            run.append(k)
            runEnd = max(runEnd, sp.y + sp.lane)
        }
        close()
        return spans.enumerated().map { k, sp in
            let w = (width - gap * CGFloat(columns[k] - 1)) / CGFloat(columns[k])
            return CGRect(x: CGFloat(column[k]) * (w + gap), y: sp.y, width: max(0, w), height: sp.h)
        }
    }
}

/// An item of a day of the week's grid, and the minutes into the day it is at.
private struct WeekItem: Identifiable {
    let ev: CalEvent
    let at: Int
    var id: String { ev.id }
}

/// Where an item's time puts it in its day of the week's grid.
private struct WeekAt: LayoutValueKey {
    static let defaultValue: CGFloat = 0
}

/// (1.3.167) How tall an item's block is for the time it lasts (0: as tall as its name needs).
private struct WeekLength: LayoutValueKey {
    static let defaultValue: CGFloat = 0
}

/// (1.3.181) How tall an item is as it rests, for the columns the day's items share: a plan whose foot is being pulled
/// keeps its column, and the others theirs, until it is let go (0: as WeekLength).
private struct WeekLane: LayoutValueKey {
    static let defaultValue: CGFloat = 0
}

/// (1.3.169) A row of To Do dragged over a day of the week: the minute it would be planned for; let go, on its way.
struct TodoDrop: Equatable {
    var day: Int
    var minutes: Int
    var placed = false
}

/// (1.3.169) The row of To Do being dragged, by its id: set as the drag starts (onDrag), read where it is let go.
enum TodoDragging {
    nonisolated(unsafe) static var id: String?
}

/// (1.3.169) A day of the week taking a row of To Do: where it would go follows the pointer a quarter of an hour at a time
/// (the hour's block centred under it), and let go, the work is planned there. (1.3.170) Over the whole day's hours as
/// they scroll: held near the top or the bottom of the hours in view, they scroll on under it.
private struct TodoDropDelegate: DropDelegate {
    let day: Int
    let hourHeight: CGFloat
    let enabled: Bool
    let scroller: WeekScroller
    @Binding var drop: TodoDrop?
    let put: (String, Int) -> Void

    func validateDrop(info: DropInfo) -> Bool { enabled && TodoDragging.id != nil && info.hasItemsConforming(to: [.text]) }
    func dropEntered(info: DropInfo) { follow(info) }
    func dropUpdated(info: DropInfo) -> DropProposal? {
        follow(info)
        return DropProposal(operation: .copy)
    }
    func dropExited(info: DropInfo) {
        if drop?.day == day, drop?.placed != true { drop = nil }
    }
    func performDrop(info: DropInfo) -> Bool {
        guard enabled, let id = TodoDragging.id else {
            drop = nil
            return false
        }
        TodoDragging.id = nil
        put(id, minutes(info))
        return true
    }

    private func minutes(_ info: DropInfo) -> Int {
        let into = Double(info.location.y / hourHeight) * 60 - 30
        let snapped = Int((into / 15).rounded()) * 15
        return min(23 * 60, max(0, snapped))
    }

    private func follow(_ info: DropInfo) {
        guard enabled, TodoDragging.id != nil else { return }
        MainActor.assumeIsolated { scroller.nudge(at: info.location.y) }
        let next = TodoDrop(day: day, minutes: minutes(info))
        guard next != drop else { return }
        let newDay = drop?.day != day // (1.3.185: the trackpad ticks as another day is reached, not every quarter hour)
        drop = next
        if newDay { NSHapticFeedbackManager.defaultPerformer.perform(.alignment, performanceTime: .now) }
    }
}

/// (1.3.170, the web's 2.99.112) A day of the month or of the agenda taking a row of To Do: the day held over is said
/// (`over`, it rings itself), and let go, the work is planned at that day's first free time.
private struct TodoDayDropDelegate: DropDelegate {
    let day: Date
    let enabled: Bool
    @Binding var over: Date?
    let put: (String) -> Void

    func validateDrop(info: DropInfo) -> Bool { enabled && TodoDragging.id != nil && info.hasItemsConforming(to: [.text]) }
    func dropEntered(info: DropInfo) {
        guard enabled, TodoDragging.id != nil, over != day else { return }
        over = day
        NSHapticFeedbackManager.defaultPerformer.perform(.alignment, performanceTime: .now)
    }
    func dropUpdated(info: DropInfo) -> DropProposal? { DropProposal(operation: .copy) }
    func dropExited(info: DropInfo) {
        if over == day { over = nil }
    }
    func performDrop(info: DropInfo) -> Bool {
        guard enabled, let id = TodoDragging.id else {
            over = nil
            return false
        }
        TodoDragging.id = nil
        put(id)
        return true
    }
}

/// (1.3.170) What planDay answers: when the work was planned, and in words ("Planned Wed 8:00 AM"). (Why not is its
/// error, which the engine throws.)
private struct PlanDayReply: Decodable {
    var at: String?
    var words: String?
}

/// (1.3.167) A plan's block dragged on the week: by how many days and minutes; dropped, on its way to the page.
/// (1.3.185) Where the pointer has it to the point is PlanFollow's, so this changes only a quarter hour or a day at a time.
struct PlanDrag: Equatable {
    var id: String
    var days: Int
    var minutes: Int
    /// (1.3.172) How far into the block it was taken, in minutes: where the pointer is, for the plan it is held over.
    var grab = 0
    var dropped = false
}

/// (1.3.172) A plan's foot pulled on the week: how long the plan would be; let go, on its way to the page.
struct PlanStretch: Equatable {
    var id: String
    var mins: Int
    var dropped = false
}

/// (1.3.185) Where the pointer has a dragged plan's block, to the point. Kept apart from the week's own state and watched
/// (Observation) by each plan's block for its own id alone: following the pointer draws that one block again, not the
/// whole week and To Do beside it.
@Observable
final class PlanFollow {
    private(set) var id: String?
    private(set) var offset: CGSize = .zero

    func move(_ id: String, to offset: CGSize) {
        if self.id != id { self.id = id } // (the other blocks watch the id: told only as a drag starts or ends)
        self.offset = offset
    }

    func clear() {
        if id != nil { id = nil }
        if offset != .zero { offset = .zero }
    }
}

/// (1.3.185) A plan's block where the pointer has it while held (PlanFollow), and where it settles once let go.
private struct PlanFollowing<Content: View>: View {
    let follow: PlanFollow
    let id: String
    let settled: CGSize?
    @ViewBuilder let content: Content

    var body: some View {
        content.offset(settled ?? (follow.id == id ? follow.offset : .zero))
    }
}

/// The calendars shown, each with its colour: your own courses on, the rest off until turned on — ten at most, as Canvas
/// allows (the others wait, greyed, until one is turned off).
/// (1.3.186) Every calendar Simpl can show, chosen here alone: Canvas's; each calendar account's (Sync's accounts,
/// CalendarAccounts); and those linked by their address. Sync only connects them — signs in and out, links and
/// unlinks, sends Simpl out — and a button here opens it.
private struct CalendarsPopover: View {
    let choices: [CalendarChoice]
    /// Sync opened (this popover closed first).
    let openSync: () -> Void
    let changed: () -> Void
    @EnvironmentObject private var engine: Engine
    @ObservedObject private var accounts = CalendarAccounts.shared
    @State private var on: Set<String> = []
    /// The calendars linked by their address (native-app.js calSync), read as the popover opens.
    @State private var links: [SyncLink] = []

    private var own: [CalendarChoice] { choices.filter { $0.own == true } }
    private var other: [CalendarChoice] { choices.filter { $0.own != true } }
    private var full: Bool { on.count >= 10 }
    private var shown: Int {
        on.count + accounts.accounts.filter { !$0.signedOut }.reduce(0) { $0 + $1.calendars.filter(\.on).count } + links.filter(\.on).count
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(alignment: .firstTextBaseline) {
                Text("Calendars").font(.sHeadline)
                Spacer(minLength: 8)
                Text("\(shown) shown")
                    .font(.sFootnote.monospacedDigit())
                    .foregroundStyle(.secondary)
                    .contentTransition(.numericText(value: Double(shown)))
            }
            if choices.isEmpty && accounts.accounts.isEmpty && links.isEmpty {
                Text("No calendars to show.").foregroundStyle(.secondary)
            } else {
                ScrollView {
                    VStack(alignment: .leading, spacing: 9) {
                        if !choices.isEmpty {
                            heading(engine.onBrightspace ? "Brightspace" : "Canvas", detail: nil, first: true)
                            ForEach(own) { canvasRow($0) }
                            if !other.isEmpty {
                                Text("Other Calendars")
                                    .font(.sFootnote.weight(.semibold))
                                    .foregroundStyle(.secondary)
                                    .padding(.top, own.isEmpty ? 0 : 4)
                                ForEach(other) { canvasRow($0) }
                            }
                            Text(full ? "Ten at most: turn one off to show another." : (engine.onBrightspace ? "At most 10 of these show at once." : "Canvas shows at most 10 of these at once."))
                                .font(.sFootnote)
                                .foregroundStyle(full ? Color.orange : Color.secondary)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                        ForEach(accounts.accounts) { a in
                            heading(a.provider.name, detail: a.name, first: choices.isEmpty && a.id == accounts.accounts.first?.id)
                            if a.calendars.isEmpty {
                                Text(a.signedOut ? "Sign in again in Sync to choose its calendars." : "Reading its calendars…")
                                    .font(.sFootnote)
                                    .foregroundStyle(.secondary)
                            }
                            ForEach(a.calendars) { c in accountRow(c, of: a) }
                        }
                        if !links.isEmpty {
                            heading("Linked by Address", detail: nil, first: choices.isEmpty && accounts.accounts.isEmpty)
                            ForEach(links) { linkRow($0) }
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                // (1.3.82: shorter where the screen has less room for the popover — a popover of one height only, kept
                // shorter by AppKit, is sized back up by SwiftUI pass after pass: ExternalToolsView's list)
                .frame(minHeight: min(84, listHeight), idealHeight: listHeight, maxHeight: listHeight)
            }
            Divider()
            Button(action: openSync) {
                Label("Add or Remove Calendars in Sync…", systemImage: "arrow.triangle.2.circlepath")
                    .font(.sCallout)
            }
            .buttonStyle(.borderless)
        }
        .font(.sBody)
        .padding(18)
        .frame(width: 340)
        .animation(Motion.snappy, value: full)
        .animation(Motion.snappy, value: links)
        .onAppear { on = Set(choices.filter { $0.on }.map { $0.code }) }
        .task { await readLinks() }
    }

    private var listHeight: CGFloat {
        var h: CGFloat = 0
        if !choices.isEmpty { h += 34 + CGFloat(choices.count) * 28 + (other.isEmpty ? 0 : 30) + 30 }
        for a in accounts.accounts { h += 40 + CGFloat(max(1, a.calendars.count)) * 28 }
        if !links.isEmpty { h += 34 + CGFloat(links.count) * 28 }
        return min(max(h, 28), 460)
    }

    /// A section's name — the place its calendars come from — and who, for an account.
    private func heading(_ title: String, detail: String?, first: Bool) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 6) {
            Text(title.uppercased())
                .font(.sCaption.weight(.semibold))
                .foregroundStyle(.secondary)
            if let detail, !detail.isEmpty {
                Text(detail)
                    .font(.sCaption)
                    .foregroundStyle(.tertiary)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
        }
        .padding(.top, first ? 0 : 10)
    }

    private func dot(_ color: String?) -> some View {
        Circle()
            .fill(Color(hex: color))
            .frame(width: 11, height: 11)
    }

    private func canvasRow(_ c: CalendarChoice) -> some View {
        Toggle(isOn: Binding(get: { on.contains(c.code) }, set: { set(c, $0) })) {
            HStack(spacing: 8) {
                dot(c.color)
                Text(c.name).font(.sBody).lineLimit(1)
            }
        }
        .toggleStyle(.checkbox)
        .disabled(!on.contains(c.code) && full)
        .help(c.name)
    }

    private func accountRow(_ c: AccountCalendar, of a: CalendarAccount) -> some View {
        Toggle(isOn: Binding(get: { c.on }, set: { value in
            accounts.show(c.id, of: a.id, value)
            changed()
        })) {
            HStack(spacing: 8) {
                dot(c.color)
                Text(c.name).font(.sBody).lineLimit(1)
                if let g = c.group, !g.isEmpty {
                    Text(g).font(.sCaption).foregroundStyle(.secondary).lineLimit(1)
                }
            }
        }
        .toggleStyle(.checkbox)
        .disabled(a.signedOut)
        .help(c.name)
    }

    private func linkRow(_ l: SyncLink) -> some View {
        Toggle(isOn: Binding(get: { l.on }, set: { value in
            if let i = links.firstIndex(where: { $0.id == l.id }) { links[i].on = value }
            Task {
                _ = try? await engine.call("calShow", ["id": l.id, "on": value], as: ActReply.self)
                changed()
            }
        })) {
            HStack(spacing: 8) {
                dot(l.color)
                Text(l.name).font(.sBody).lineLimit(1)
                if !l.error.isEmpty {
                    Image(systemName: "exclamationmark.triangle.fill")
                        .font(.system(size: 10, weight: .bold))
                        .foregroundStyle(.red)
                        .help(l.error)
                }
            }
        }
        .toggleStyle(.checkbox)
        .help(l.name)
    }

    private func readLinks() async {
        guard let got = try? await engine.call("calSync", as: SyncData.self) else { return }
        links = got.links
    }

    private func set(_ c: CalendarChoice, _ value: Bool) {
        if value && on.count >= 10 { return }
        if value { on.insert(c.code) } else { on.remove(c.code) }
        let codes = choices.map { $0.code }.filter { on.contains($0) }
        Task {
            await engine.act("setCalendars", ["codes": codes])
            changed()
        }
    }
}

// MARK: - The week's hours, scrolled (1.3.170)

/// (1.3.170, the web's 2.99.112) The week's hours as they scroll: the Mac's own scroll view under SwiftUI's, found from
/// inside it (`WeekScrollProbe`) — where the hours in view begin and how tall their view is, for what waits at its edges
/// (`WeekPeeks`, the one thing that follows the scroll); scrolled to a time, or after the hours were scaled about the
/// pointer, once they are laid out at their new height; kept where they were left for the next scroll view (the week
/// drawn again, another week, the week again), and opened at `opening` the first time. ⌘- or ⌃-scrolled over, it tells
/// `zoom` by how much and where; a row of To Do held near its top or its bottom scrolls it on (`nudge`).
@MainActor
final class WeekScroller: ObservableObject {
    @Published private(set) var top: CGFloat = 0
    @Published private(set) var height: CGFloat = 0
    /// Where a scroll view opens the first time, from its top.
    var opening: () -> CGFloat = { 0 }
    /// ⌘- or ⌃-scroll over the hours: by how much an hour grows, and where the pointer is from the view's top.
    var zoom: (CGFloat, CGFloat) -> Void = { _, _ in }
    private weak var view: NSScrollView?
    private var watches: [NSObjectProtocol] = []
    private var wheel: Any?
    /// Where the hours were left (nil: nowhere yet), and whether the scroll view now up has been put there.
    private var kept: CGFloat?
    private var placed = false
    /// Where to scroll once the hours are laid out again at a new height.
    private var pending: CGFloat?

    /// The scroll view the probe is in, taken up: its scroll followed, the wheel watched over it, and it put where the
    /// hours were left.
    func attach(_ probe: NSView) {
        guard let sv = probe.enclosingScrollView, sv !== view else { return }
        detach()
        view = sv
        placed = false
        let clip = sv.contentView
        clip.postsBoundsChangedNotifications = true
        clip.postsFrameChangedNotifications = true
        let center = NotificationCenter.default
        watches = [NSView.boundsDidChangeNotification, NSView.frameDidChangeNotification].map { name in
            center.addObserver(forName: name, object: clip, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated {
                    if let self { self.read() }
                }
            }
        }
        wheel = NSEvent.addLocalMonitorForEvents(matching: .scrollWheel) { [weak self] e in
            guard let self else { return e }
            return self.scrolled(e)
        }
        let at = kept ?? opening()
        DispatchQueue.main.async { [weak self] in
            guard let self, self.view === sv else { return }
            self.scroll(to: at)
        }
    }

    /// Let go of (the calendar gone, or another scroll view taken up).
    func detach() {
        for w in watches { NotificationCenter.default.removeObserver(w) }
        watches = []
        if let wheel { NSEvent.removeMonitor(wheel) }
        wheel = nil
        view = nil
    }

    /// Scrolled to `y` from the hours' top (kept within them), at once or on a short ease.
    func scroll(to y: CGFloat, animated: Bool = false) {
        guard let sv = view, let doc = sv.documentView else { return }
        let clip = sv.contentView
        let most = max(0, doc.frame.height - clip.bounds.height)
        let t = min(max(0, y), most)
        let origin = NSPoint(x: clip.bounds.minX, y: doc.isFlipped ? t : most - t)
        if animated {
            NSAnimationContext.runAnimationGroup { ctx in
                ctx.duration = 0.28
                ctx.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
                ctx.allowsImplicitAnimation = true
                clip.animator().setBoundsOrigin(origin)
            }
        } else {
            clip.scroll(to: origin)
        }
        sv.reflectScrolledClipView(clip)
        placed = true
        kept = t
        read()
    }

    /// Scrolled to `y` once the hours have been laid out at their new height (the probe under them says so).
    func scrollAfterLayout(to y: CGFloat) {
        pending = y
        kept = y
    }

    /// The hours laid out again (the probe's size changed): a scroll waiting for it made.
    func resized() {
        guard let y = pending else { return }
        pending = nil
        DispatchQueue.main.async { [weak self] in self?.scroll(to: y) }
    }

    /// A row of To Do held at `y` (from the hours' top) near the top or the bottom of the hours in view: on a little.
    func nudge(at y: CGFloat) {
        guard height > 0 else { return }
        if y - top < 36 { scroll(to: top - 14) } else if top + height - y < 36 { scroll(to: top + 14) }
    }

    private func read() {
        guard let sv = view, let doc = sv.documentView else { return }
        let clip = sv.contentView
        let h = clip.bounds.height
        let t = doc.isFlipped ? clip.bounds.minY : doc.frame.height - clip.bounds.maxY
        if abs(t - top) > 0.5 { top = t }
        if abs(h - height) > 0.5 { height = h }
        if placed && pending == nil { kept = t } // (never a new scroll view's top before it was put where the hours were)
    }

    /// The wheel with ⌘ or ⌃ held over the hours: theirs (the hours scaled), and nobody else's.
    private func scrolled(_ e: NSEvent) -> NSEvent? {
        guard let sv = view, e.window === sv.window, !e.modifierFlags.intersection([.command, .control]).isEmpty else { return e }
        let p = sv.convert(e.locationInWindow, from: nil)
        guard sv.bounds.contains(p) else { return e }
        let y = sv.isFlipped ? p.y : sv.bounds.height - p.y
        let d = e.hasPreciseScrollingDeltas ? e.scrollingDeltaY : e.scrollingDeltaY * 6
        if d != 0 { zoom(exp(d * 0.012), y) }
        return nil
    }
}

/// An unseen view under the week's hours, inside their scroll view: it finds it for `WeekScroller`, and says when the
/// hours are laid out at a new height.
private struct WeekScrollProbe: NSViewRepresentable {
    let scroller: WeekScroller

    func makeNSView(context: Context) -> WeekProbeView {
        let v = WeekProbeView()
        v.scroller = scroller
        return v
    }

    func updateNSView(_ v: WeekProbeView, context: Context) {
        v.scroller = scroller
        v.look()
    }
}

final class WeekProbeView: NSView {
    weak var scroller: WeekScroller?

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        look()
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        scroller?.resized()
    }

    /// Its scroll view taken up, once it is in one (a moment on: the view is put in its place first).
    func look() {
        guard window != nil else { return }
        DispatchQueue.main.async { [weak self] in
            guard let self, self.window != nil else { return }
            self.scroller?.attach(self)
        }
    }
}

/// An item of a day of the week, down its hours: where it starts and ends, its colour and its name.
private struct WeekPeek: Identifiable {
    let id: String
    let start: CGFloat
    let end: CGFloat
    let color: Color
    let title: String
}

/// (1.3.170, the web's 2.99.112) What lies above or below the hours in view, squished at that edge of its day — a thin
/// bar in its colour, four at most at each edge — a press scrolling to it. Drawn over the hours' view, it follows their
/// scroll; nothing else of the week does.
private struct WeekPeeks: View {
    @ObservedObject var scroller: WeekScroller
    let days: [[WeekPeek]]
    let gutter: CGFloat
    let go: (CGFloat) -> Void

    var body: some View {
        let top = scroller.top, bottom = scroller.top + scroller.height
        HStack(spacing: 0) {
            Color.clear
                .frame(width: gutter)
                .allowsHitTesting(false)
            ForEach(Array(days.enumerated()), id: \.offset) { _, items in
                let above = Array(items.filter { $0.end <= top + 6 }.suffix(4))
                let below = Array(items.filter { $0.start >= bottom - 6 }.prefix(4))
                VStack(spacing: 2) {
                    ForEach(above) { p in
                        WeekPeekBar(peek: p) { go(p.start) }
                            .transition(.opacity.combined(with: .scale(scale: 0.5, anchor: .top)))
                    }
                    Spacer(minLength: 0)
                    ForEach(below) { p in
                        WeekPeekBar(peek: p) { go(p.start) }
                            .transition(.opacity.combined(with: .scale(scale: 0.5, anchor: .bottom)))
                    }
                }
                .padding(.horizontal, 4)
                .padding(.vertical, 3)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .animation(.easeOut(duration: 0.18), value: above.map(\.id))
                .animation(.easeOut(duration: 0.18), value: below.map(\.id))
            }
        }
        .opacity(scroller.height > 0 ? 1 : 0)
    }
}

/// One of them: 6 points tall, a little taller under the pointer.
private struct WeekPeekBar: View {
    let peek: WeekPeek
    let go: () -> Void
    @State private var hover = false

    var body: some View {
        Button(action: go) {
            RoundedRectangle(cornerRadius: 3, style: .continuous)
                .fill(peek.color.opacity(0.3))
                .overlay(alignment: .leading) {
                    RoundedRectangle(cornerRadius: 1.5, style: .continuous)
                        .fill(peek.color)
                        .frame(width: 3)
                }
                .frame(height: hover ? 9 : 6)
                .shadow(color: .black.opacity(0.12), radius: 1.5, y: 1)
                .contentShape(Rectangle().inset(by: -3))
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
        .animation(.easeOut(duration: 0.14), value: hover)
        .help(peek.title)
        .accessibilityLabel("Scroll to \(peek.title)")
    }
}

// MARK: - The head's controls (1.3.170)

/// Week, Month and Agenda, as the web's segmented control has them: the one chosen lit, sliding to the one pressed; Week,
/// chosen, carries a small chevron, and pressed again offers its five days or seven (`weekMenu`, under it).
private struct CalendarModeSwitch: View {
    let mode: String
    let weekDays: Int
    let pick: (String) -> Void
    let weekMenu: (NSView?) -> Void
    @Namespace private var lit
    @State private var weekAnchor = PreviewAnchor()

    private struct Choice: Identifiable {
        let id: String
        let title: String
    }

    private static let modes = [Choice(id: "week", title: "Week"), Choice(id: "month", title: "Month"), Choice(id: "list", title: "Agenda")]

    var body: some View {
        HStack(spacing: 2) {
            ForEach(Self.modes) { m in
                let on = mode == m.id
                Button {
                    if m.id == "week" && on { weekMenu(weekAnchor.view) } else { pick(m.id) }
                } label: {
                    HStack(spacing: 4) {
                        Text(m.title)
                        if m.id == "week" && on {
                            Image(systemName: "chevron.down")
                                .font(.system(size: 8.5, weight: .bold))
                                .opacity(0.55)
                                .transition(.opacity)
                        }
                    }
                    .font(.sCallout.weight(on ? .semibold : .medium))
                    .foregroundStyle(on ? Color.primary : Color.secondary)
                    .padding(.horizontal, 12)
                    .frame(height: 26)
                    .background {
                        if on {
                            Capsule(style: .continuous)
                                .fill(Theme.card)
                                .shadow(color: .black.opacity(0.14), radius: 1.5, y: 1)
                                .matchedGeometryEffect(id: "lit", in: lit)
                        }
                    }
                    .contentShape(Capsule())
                }
                .buttonStyle(.plain)
                .background {
                    if m.id == "week" { PreviewProbe(anchor: weekAnchor).allowsHitTesting(false) }
                }
                .help(help(m.id, on: on))
                .accessibilityAddTraits(on ? .isSelected : [])
            }
        }
        .padding(3)
        .background(Capsule(style: .continuous).fill(Color.primary.opacity(0.07)))
        .animation(Motion.snappy, value: mode)
        .fixedSize()
    }

    private func help(_ key: String, on: Bool) -> String {
        switch key {
        case "week": return on ? "\(weekDays) days: press again for \(weekDays == 5 ? "seven" : "five")" : "A week by the hour"
        case "list": return "The next three weeks, or the days you pick"
        default: return "A month"
        }
    }
}

/// A tool's label in the head: its symbol and its words, or (the page narrower) its symbol alone.
private struct ToolLabel: LabelStyle {
    let words: Bool

    @ViewBuilder
    func makeBody(configuration: Configuration) -> some View {
        if words {
            HStack(spacing: 6) {
                configuration.icon
                configuration.title
            }
        } else {
            HoverWordsBody(icon: configuration.icon) // (1.3.172: its name beside it while the pointer rests on it)
        }
    }
}
