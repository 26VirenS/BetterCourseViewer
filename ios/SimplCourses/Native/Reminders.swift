import SwiftUI
import UserNotifications

/// Due-date reminders (1.4.8): the phone's own alerts for work still to hand in, set on this iPhone
/// (UNUserNotificationCenter) from the page's `reminders` call. Each one fires at its minute whether or not
/// Simpl is open or the phone is online, and nothing is sent anywhere. They are set again whenever the app
/// has fresh work: when the app's screens come up, after a change (a tick, a hand-in), and on coming back to
/// the foreground. iOS keeps 64 at most for an app: the soonest 60 are set.
@MainActor
final class Reminders: NSObject, ObservableObject, UNUserNotificationCenterDelegate {
    static let shared = Reminders()

    /// How long before the due time: one alert, or the day before and the hour before.
    enum Lead: String, CaseIterable, Identifiable {
        case dayAndHour, day, threeHours, hour

        var id: String { rawValue }
        var minutes: [Int] {
            switch self {
            case .dayAndHour: return [1440, 60]
            case .day: return [1440]
            case .threeHours: return [180]
            case .hour: return [60]
            }
        }
        var label: String {
            switch self {
            case .dayAndHour: return "1 day and 1 hour before"
            case .day: return "1 day before"
            case .threeHours: return "3 hours before"
            case .hour: return "1 hour before"
            }
        }
    }

    private static let onKey = "SimplRemindersOn"
    private static let leadKey = "SimplRemindersLead"
    private static let prefix = "simpl.due."
    private static let cap = 60

    @Published private(set) var on = UserDefaults.standard.bool(forKey: Reminders.onKey)
    @Published var lead = Lead(rawValue: UserDefaults.standard.string(forKey: Reminders.leadKey) ?? "") ?? .dayAndHour {
        didSet { UserDefaults.standard.set(lead.rawValue, forKey: Reminders.leadKey) }
    }
    /// Notifications for Simpl turned off in iOS Settings: the switch says so and offers the way there.
    @Published private(set) var refused = false
    /// How many are set right now (Settings says it).
    @Published private(set) var scheduled = 0
    /// A reminder pressed: the address of its work, opened once the app's screens are up (RootView).
    @Published var opening: String?

    private var running: Task<Void, Never>?

    private var center: UNUserNotificationCenter { .current() }

    /// The switch turned on: iOS asks the student once; refused, it stays off and says where to allow it.
    func enable(_ engine: Engine) async {
        let granted = (try? await center.requestAuthorization(options: [.alert, .sound])) ?? false
        await checkRefused()
        guard granted else {
            setOn(false)
            return
        }
        setOn(true)
        await reschedule(engine)
    }

    func disable() {
        setOn(false)
        Task { await clear() }
    }

    /// Whether iOS has notifications for Simpl turned off (after it was asked).
    func checkRefused() async {
        let s = await center.notificationSettings()
        refused = s.authorizationStatus == .denied
    }

    /// Every reminder taken away (sign out, another school: someone else's work is not this account's).
    func clear() async {
        let ids = await center.pendingNotificationRequests().map(\.identifier).filter { $0.hasPrefix(Reminders.prefix) }
        center.removePendingNotificationRequests(withIdentifiers: ids)
        scheduled = 0
    }

    /// The reminders set again from what Canvas says now: the old ones taken away, the soonest new ones set.
    func reschedule(_ engine: Engine) async {
        guard on else { return }
        running?.cancel()
        let task = Task { await self.run(engine) }
        running = task
        await task.value
    }

    private func run(_ engine: Engine) async {
        guard (await center.notificationSettings()).authorizationStatus == .authorized,
              let data = try? await engine.call("reminders", as: RemindersData.self), !Task.isCancelled else { return }
        let now = Date()
        var requests: [UNNotificationRequest] = []
        for item in data.items {
            guard let due = QuizTime.date(item.due) else { continue }
            for m in lead.minutes {
                let fire = due.addingTimeInterval(-Double(m) * 60)
                guard fire > now.addingTimeInterval(60) else { continue }
                requests.append(request(item, due: due, fire: fire, minutes: m))
            }
        }
        requests.sort { fireDate($0) < fireDate($1) }
        await clear()
        guard !Task.isCancelled else { return }
        for r in requests.prefix(Reminders.cap) { try? await center.add(r) }
        scheduled = min(requests.count, Reminders.cap)
    }

    private func fireDate(_ r: UNNotificationRequest) -> Date {
        (r.trigger as? UNCalendarNotificationTrigger)?.nextTriggerDate() ?? .distantFuture
    }

    private func request(_ item: ReminderItem, due: Date, fire: Date, minutes: Int) -> UNNotificationRequest {
        let c = UNMutableNotificationContent()
        c.title = item.title
        if let course = item.course, !course.isEmpty { c.subtitle = course }
        let time = due.formatted(date: .omitted, time: .shortened)
        let when: String
        switch minutes {
        case 1440: when = "due tomorrow at \(time)"
        case 60: when = "due in 1 hour, at \(time)"
        default: when = "due in \(minutes / 60) hours, at \(time)"
        }
        c.body = item.kind == "My task" ? when.prefix(1).uppercased() + when.dropFirst() : "\(item.kind) \(when)"
        c.sound = .default
        c.threadIdentifier = item.course ?? "tasks"
        if let url = item.url { c.userInfo = ["url": url] }
        let parts = Calendar.current.dateComponents([.year, .month, .day, .hour, .minute], from: fire)
        let trigger = UNCalendarNotificationTrigger(dateMatching: parts, repeats: false)
        return UNNotificationRequest(identifier: "\(Reminders.prefix)\(item.id).\(minutes)", content: c, trigger: trigger)
    }

    private func setOn(_ value: Bool) {
        on = value
        UserDefaults.standard.set(value, forKey: Reminders.onKey)
    }

    // MARK: - UNUserNotificationCenterDelegate

    /// A reminder due while Simpl is open: shown as a banner all the same.
    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification) async -> UNNotificationPresentationOptions {
        [.banner, .list, .sound]
    }

    /// A reminder pressed: its work opens (once the app's screens are up).
    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse) async {
        let url = response.notification.request.content.userInfo["url"] as? String
        guard let url, !url.isEmpty else { return }
        await MainActor.run { Reminders.shared.opening = url }
    }
}

/// The page's `reminders` call: the work still to hand in, soonest first.
struct RemindersData: Decodable {
    var items: [ReminderItem]
}

struct ReminderItem: Decodable {
    var id: String
    var title: String
    var course: String?
    var kind: String
    var due: String
    var url: String?
}
