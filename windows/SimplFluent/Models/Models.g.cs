// Generated from the Swift models (scripts: windows/tools/swift2cs.py). Every field the page may leave out is
// optional, so a page a version apart still reads.
#nullable enable
namespace Simpl.Models;

public sealed class WorkFlag
{
    public string Word { get; set; } = "";
    public string? Kind { get; set; }
}

public sealed class Person
{
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Pronouns { get; set; }
    public string? Initials { get; set; }
    public string? Avatar { get; set; }
}

public sealed class Snapshot
{
    public Person? Me { get; set; }
    public int? NotifUnread { get; set; }
    public int? InboxUnread { get; set; }
    public bool? Dark { get; set; }
    public string? Site { get; set; }
    public string? Host { get; set; }
    public string? Version { get; set; }
    public bool? SetupDone { get; set; }
    public string? Lms { get; set; }
    public SchoolLogo? Logo { get; set; }
}

public sealed class SchoolLogo
{
    public string Url { get; set; } = "";
    public bool? Square { get; set; }
    public string? Bg { get; set; }
}

public sealed class WorkRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Sub { get; set; }
    public string? Course { get; set; }
    public string? Color { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public bool Done { get; set; }
    public WorkFlag? Flag { get; set; }
    public string? Url { get; set; }
    public bool? Custom { get; set; }
    public string? CourseName { get; set; }
    public string? Meta { get; set; }
    public string? When { get; set; }
    public int? Pri { get; set; }
    public string? PriShort { get; set; }
    public string? Type { get; set; }
    public int? Series { get; set; }
    public string? Kind { get; set; }
    public string? Pts { get; set; }
    // (2.99.107) the time it is planned for, pulled to (content/app/plan.js): "Planned Tue 4:00 PM"
    public WorkPlan? Plan { get; set; }
    // (2.99.112) in To Do & Calendar's panel (todo's `panel`): dismissed off To Do, listed with what is completed to restore
    public bool? Dismissed { get; set; }
}

// (2.99.107) a piece of work's plan: when, for how long, and the words its pill says
public sealed class WorkPlan
{
    public string At { get; set; } = "";
    public int Mins { get; set; }
    public bool? Past { get; set; }
    public string Words { get; set; } = "";
}

// (2.99.107) Calendar → Sync as native-app.js `calSync` reads it: Simpl's own calendar for a calendar app (2.99.110, in
// place of Canvas's feed and Send plans) and the student's own calendars linked by their private address
public sealed class SyncData
{
    public SimplFeed Simpl { get; set; } = new();
    public List<SyncLink> Links { get; set; } = new();
}

// (2.99.110) Simpl's own calendar as native-app.js `feedState` (feedOn, feedOff, feedSend) says it: whether it is on, its
// private link as each calendar app takes it, when it was last sent and how many events it held, or why not
public sealed class SimplFeed
{
    public bool On { get; set; }
    public string? Link { get; set; }
    public string? Apple { get; set; }
    public string? Google { get; set; }
    public string? Office { get; set; }
    public string? Outlook { get; set; }
    public string? Sent { get; set; }
    public int Count { get; set; }
    // why it could not be made or sent (`problem`, as an answer's own `error` is how the apps know a call failed)
    public string Problem { get; set; } = "";
}

public sealed class SyncLink
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Color { get; set; }
    public bool On { get; set; }
    public string? Read { get; set; }
    public int Count { get; set; }
    public string Error { get; set; } = "";
}

// (2.99.107) what linking a calendar answers: its name and how many events it holds, or why not
public sealed class CalLinkReply
{
    public bool? Ok { get; set; }
    public string? Name { get; set; }
    public int? Count { get; set; }
    public string? Error { get; set; }
}

// (2.99.107) the free times a piece of work could be planned in (native-app.js `planSlots`)
public sealed class PlanSlots
{
    public string? Title { get; set; }
    public int? Mins { get; set; }
    public List<int>? Lengths { get; set; }
    public string? Planned { get; set; }
    public string? Due { get; set; }
    public string? DueWords { get; set; }
    public List<PlanSlot>? Slots { get; set; }
    public string? Error { get; set; }
}

// (2.99.112) a piece of work let go on a day of the month or the agenda (native-app.js `planDay`): when it was planned
public sealed class PlanDay
{
    public bool? Ok { get; set; }
    public string? At { get; set; }
    public string? Words { get; set; }
    public WorkPlan? Plan { get; set; }
}

// (2.99.112) how much is still to do in the courses chosen (native-app.js `todoCount`), for To Do & Calendar's row
public sealed class TodoCount
{
    public int? Open { get; set; }
}

public sealed class PlanSlot
{
    public string At { get; set; } = "";
    public string Day { get; set; } = "";
    public string Time { get; set; } = "";
}

public sealed class Counter
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int? Value { get; set; }
    public string? Tone { get; set; }
    public bool? Pending { get; set; }
    public string? Note { get; set; }
    public string? FullLabel { get; set; }
    public string? FullNote { get; set; }
}

public sealed class TodayList
{
    public string Heading { get; set; } = "";
    public string? Note { get; set; }
    public List<WorkRow> Rows { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class LoadRow
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Color { get; set; }
    public int Done { get; set; }
    public int Total { get; set; }
    public string? Text { get; set; }
}

public sealed class Today
{
    public string? DateLine { get; set; }
    public Person? Me { get; set; }
    public int? NotifUnread { get; set; }
    public List<Counter> Counters { get; set; } = new();
    public TodayList List { get; set; } = new();
    public List<LoadRow>? Load { get; set; }
    public int? Idle { get; set; }
    public bool? HasCourses { get; set; }
    public string? LoadCaption { get; set; }
}

public sealed class TodayCounts
{
    public int? Overdue { get; set; }
    public int? Graded { get; set; }
    public string? OverdueNote { get; set; }
    public string? GradedNote { get; set; }
    public string? GradedFullNote { get; set; }
}

public sealed class SheetRow
{
    public string Title { get; set; } = "";
    public string? Sub { get; set; }
    public string? Color { get; set; }
    public string? Url { get; set; }
    public bool? Quiet { get; set; }
    public string? Key { get; set; }
    public bool? Clearable { get; set; }
    public string? Course { get; set; }
    public string? Kind { get; set; }
    public string? Detail { get; set; }
    public string? When { get; set; }
    public string? Date { get; set; }
}

public sealed class SheetSection
{
    public string Title { get; set; } = "";
    public bool? Quiet { get; set; }
    public List<SheetRow> Rows { get; set; } = new();
}

public sealed class ItemsSheetData
{
    public string Title { get; set; } = "";
    public string? Note { get; set; }
    public string? Empty { get; set; }
    public List<SheetSection> Sections { get; set; } = new();
}

public sealed class CourseRow
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    public string? Nickname { get; set; }
    public string? Original { get; set; }
    public string? Color { get; set; }
    public double? Score { get; set; }
    public string ScoreText { get; set; } = "";
    public int? Unread { get; set; }
    public string Url { get; set; } = "";
    public bool? Chosen { get; set; }
    public string? Term { get; set; }
    public string? AltName { get; set; }
    public string? Detail { get; set; }
    public string? ScoreWhy { get; set; }
    public int? ScoreWhole { get; set; }
    public string? UnreadLabel { get; set; }
}

public sealed class CoursesData
{
    public string? Sub { get; set; }
    public List<CourseRow> Rows { get; set; } = new();
    public string? Hidden { get; set; }
    public string? Empty { get; set; }
}

public sealed class CourseProgress
{
    public int Done { get; set; }
    public int Total { get; set; }
}

public sealed class TodoSection
{
    public string Title { get; set; } = "";
    public string? Note { get; set; }
    public List<WorkRow> Rows { get; set; } = new();
}

public sealed class NamedColor
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Color { get; set; }
}

public sealed class RepeatChoice
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class TodoData
{
    public string? Sub { get; set; }
    public int Pct { get; set; }
    public int Done { get; set; }
    public int Total { get; set; }
    public string Group { get; set; } = "";
    public bool ShowDone { get; set; }
    public List<TodoSection> Sections { get; set; } = new();
    public string? Empty { get; set; }
    public List<NamedColor>? Courses { get; set; }
    public List<RepeatChoice>? Repeats { get; set; }
    // (2.99.33) what is left and what is late, counted and in words: "17 left", "1 of 18 done", "1 overdue" ("" when none)
    public int? Left { get; set; }
    public int? Overdue { get; set; }
    public string? LeftText { get; set; }
    public string? DoneText { get; set; }
    public string? OverdueText { get; set; }
    // (2.99.112) in To Do & Calendar's panel: how much Canvas work was dismissed this week (listed with Show completed)
    public int? Dismissed { get; set; }
}

public sealed class GradeCategory
{
    public string Label { get; set; } = "";
    public string? Weight { get; set; }
    public string? Value { get; set; }
    public double? Pct { get; set; }
    public string? Color { get; set; }
}

public sealed class GradeRow
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    // (2.99.33) the course's first teacher; why it has no score ("Hidden by instructor", "No grade yet", ""), and whether
    // the instructor hides its total
    public string? Teacher { get; set; }
    public string? Why { get; set; }
    public bool? Hidden { get; set; }
    public string? Color { get; set; }
    public string? Url { get; set; }
    public double? Pct { get; set; }
    public string PctText { get; set; } = "";
    public string? Letter { get; set; }
    public double? Points { get; set; }
    public int Graded { get; set; }
    public int Total { get; set; }
    public bool? Own { get; set; }
    public string? Target { get; set; }
    public List<GradeCategory> Cats { get; set; } = new();
    public List<ScaleStep>? Scale { get; set; }
    public bool? OwnScale { get; set; }
    public string? AltName { get; set; }
    public string? GradedText { get; set; }
}

public sealed class ScaleStep
{
    public string Letter { get; set; } = "";
    public double Min { get; set; }
    public double Points { get; set; }
}

public sealed class GradeItem
{
    public string Course { get; set; } = "";
    public string? Color { get; set; }
    public string Name { get; set; } = "";
    public string Band { get; set; } = "";
    public double Pct { get; set; }
    public string PctText { get; set; } = "";
    public string Score { get; set; } = "";
    public string? Url { get; set; }
}

public sealed class TrendPoint
{
    public string Date { get; set; } = "";
    public double Gpa { get; set; }
    // (2.99.33) the day's figure and date as the readout writes them ("3.43", "Oct 12")
    public string? GpaText { get; set; }
    public string? DateText { get; set; }
}

public sealed class GradesData
{
    public string? Term { get; set; }
    public double? Gpa { get; set; }
    public double Goal { get; set; }
    // (2.99.33) the GPA rounded once, half up ("3.43", 3.43), the goal ("4.00"), the gap worked out from the rounded GPA
    // (signed) and in words ("0.57 below goal"), and what the GPA is made of ("From 4 courses, …")
    public double? GpaShown { get; set; }
    public string? GoalText { get; set; }
    public double? GoalGap { get; set; }
    public string? GoalLine { get; set; }
    public string? GpaNote { get; set; }
    public List<ScaleStep> Scale { get; set; } = new();
    public List<GradeRow> Rows { get; set; } = new();
    public List<GradeItem> Items { get; set; } = new();
    public Dictionary<string, int>? Counts { get; set; }
    public List<TrendPoint>? Trend { get; set; }
    public double? MinY { get; set; }
    public double? MaxY { get; set; }
    public string? GpaText { get; set; }
}

public sealed class CalEvent
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Date { get; set; }
    public string Day { get; set; } = "";
    public bool? AllDay { get; set; }
    public string? Time { get; set; }
    public string? Kind { get; set; }
    public bool? Done { get; set; }
    public bool? Missing { get; set; }
    public bool? Excused { get; set; }
    public string? Sub { get; set; }
    public string? Color { get; set; }
    public string? Url { get; set; }
    // (2.99.33) struck through only when handed in or excused; past once its due time (or its end) has gone by
    public bool? Struck { get; set; }
    public bool? Past { get; set; }
    // (2.99.95) a time reserved off Find Appointment: its reservation, which Give Back hands to Canvas
    public string? ReservationId { get; set; }
    // (2.99.107) "plan" for a time set aside for a piece of work (PlanId: the work's), "linked" for an event of a calendar
    // linked in Sync; null for Canvas's own. A timed one's end.
    public string? Layer { get; set; }
    public string? PlanId { get; set; }
    public string? End { get; set; }
    // (0.3.66, the Mac's 1.3.172) a plan's time this block is (a repeating plan's each), whether it repeats, and how many
    // tasks its group holds (1 for a plan alone)
    public string? Occurs { get; set; }
    public bool? Repeats { get; set; }
    public int? Tasks { get; set; }
}

// (2.99.95) Find Appointment: Canvas's Scheduler as native-app.js `appointments` reads it — the classes with something to
// sign up for, and each group with the time held and its times by day.
public sealed class AppointmentsData
{
    public List<AppointmentGroup> Groups { get; set; } = new();
    public List<AppointmentClass> Classes { get; set; } = new();
    public string? Error { get; set; }
}

public sealed class AppointmentClass
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Color { get; set; }
}

public sealed class AppointmentGroup
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Location { get; set; }
    public List<string> Codes { get; set; } = new();
    public string? Course { get; set; }
    public string? Color { get; set; }
    public int? Minutes { get; set; }
    public int? Max { get; set; }
    public bool? GroupSignup { get; set; }
    public string? Url { get; set; }
    public bool? Full { get; set; }
    public List<AppointmentHeld> Mine { get; set; } = new();
    public List<AppointmentDay> Days { get; set; } = new();
}

public sealed class AppointmentHeld
{
    public string Id { get; set; } = "";
    public string? ReservationId { get; set; }
    public string When { get; set; } = "";
}

public sealed class AppointmentDay
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Sub { get; set; } = "";
    public bool? Held { get; set; }
    public int? Open { get; set; }
    public List<AppointmentSlot> Slots { get; set; } = new();
}

public sealed class AppointmentSlot
{
    public string Id { get; set; } = "";
    public string Time { get; set; } = "";
    public string? Start { get; set; }
    public bool Reserved { get; set; }
    public bool Open { get; set; }
    public string? ReservationId { get; set; }
}

// (2.99.95) an action's answer, with Canvas's own words when it would not
public sealed class ActReply
{
    public bool? Ok { get; set; }
    public string? Error { get; set; }
}

public sealed class CalendarChoice
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Color { get; set; }
    public bool On { get; set; }
    public bool? Own { get; set; }
}

public sealed class CalendarData
{
    public List<CalEvent> Events { get; set; } = new();
    public List<CalendarChoice> Calendars { get; set; } = new();
    public string? Notice { get; set; }
    public string? Error { get; set; }
}

public sealed class NotifRow
{
    public string Id { get; set; } = "";
    public string Cat { get; set; } = "";
    public string CatLabel { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Sub { get; set; }
    public bool Read { get; set; }
    public string? Url { get; set; }
    public string? Color { get; set; }
}

public sealed class NotifDay
{
    public string Title { get; set; } = "";
    public List<NotifRow> Rows { get; set; } = new();
}

public sealed class NotifCat
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public int Count { get; set; }
}

public sealed class NotificationsData
{
    public int Total { get; set; }
    public int Unread { get; set; }
    public List<NotifDay> Days { get; set; } = new();
    public List<NotifCat> Cats { get; set; } = new();
    public int? Cleared { get; set; }
}

public sealed class SearchRow
{
    public string Title { get; set; } = "";
    public string? Sub { get; set; }
    public string Url { get; set; } = "";
    public string? Color { get; set; }
    public bool? External { get; set; }
    public string? Kind { get; set; }
}

public sealed class SearchGroup
{
    public string Title { get; set; } = "";
    public List<SearchRow> Rows { get; set; } = new();
}

public sealed class SearchData
{
    public List<SearchGroup> Groups { get; set; } = new();
}

public sealed class ReleaseNote
{
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
}

public sealed class Release
{
    public string Version { get; set; } = "";
    public string? Date { get; set; }
    public List<ReleaseNote> Notes { get; set; } = new();
    public string? DateText { get; set; }
}

public sealed class WhatsNewData
{
    public string? Version { get; set; }
    public List<Release> Releases { get; set; } = new();
}

public sealed class OK
{
    public bool? Ok { get; set; }
    public bool? Done { get; set; }
    public int? Overdue { get; set; }
    public bool? Dark { get; set; }
    public string? View { get; set; }
}

public sealed class Status
{
    public string Word { get; set; } = "";
    public string? Kind { get; set; }
}

public sealed class ARow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Kind { get; set; }
    public string? Sub { get; set; }
    public Status? Status { get; set; }
    public string Url { get; set; } = "";
}

public sealed class SectionLink
{
    public string Kind { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class MoreLink
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
    public bool? External { get; set; }
    public string? Tool { get; set; }
}

public sealed class PostRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Author { get; set; }
    public string? Avatar { get; set; }
    public string? When { get; set; }
    public string? Preview { get; set; }
    public bool? Unread { get; set; }
    public int? UnreadCount { get; set; }
    public int? Replies { get; set; }
    public bool? Graded { get; set; }
    public string Url { get; set; } = "";
    // (2.99.33) on Brightspace, the forum the topic is in ("Weekly Discussions"; the author is then ""); "" on Canvas
    public string? Forum { get; set; }
}

public sealed class FrontPage
{
    public string Title { get; set; } = "";
    public string? Excerpt { get; set; }
    public string? Slug { get; set; }
}

public sealed class HomeData
{
    public string Ctx { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Name { get; set; }
    public string? Sub { get; set; }
    public string? Color { get; set; }
    public string? Teachers { get; set; }
    public double? Score { get; set; }
    public string? ScoreText { get; set; }
    // (2.99.33) why there is no score ("No grade yet", "Hidden by instructor"; "" with one)
    public string? ScoreWhy { get; set; }
    public string? Letter { get; set; }
    public List<SectionLink> Sections { get; set; } = new();
    public List<MoreLink>? More { get; set; }
    public List<ARow> Open { get; set; } = new();
    public int? OpenCount { get; set; }
    public List<ARow>? Done { get; set; }
    public List<PostRow> Announcements { get; set; } = new();
    public FrontPage? Front { get; set; }
    public string? Html { get; set; }
}

public sealed class PostListData
{
    public string Title { get; set; } = "";
    public string? Context { get; set; }
    public string? Color { get; set; }
    public List<PostRow> Rows { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class PostSection
{
    public string Title { get; set; } = "";
    public List<PostRow> Rows { get; set; } = new();
}

public sealed class DiscussionsData
{
    public string Title { get; set; } = "";
    public string? Context { get; set; }
    public string? Color { get; set; }
    public List<PostSection> Sections { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class Attachment
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Preview { get; set; }
}

public sealed class Entry
{
    public string Id { get; set; } = "";
    public string Author { get; set; } = "";
    public string? Avatar { get; set; }
    public string? When { get; set; }
    public string Text { get; set; } = "";
    public string? Html { get; set; }
    public bool? Rich { get; set; }
    public int Depth { get; set; }
    public string? Parent { get; set; }
    public bool? Deleted { get; set; }
    public List<Attachment>? Attachments { get; set; }
}

public sealed class TopicData
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Context { get; set; }
    public string? Color { get; set; }
    public string? Author { get; set; }
    public string? Avatar { get; set; }
    public string? When { get; set; }
    public string Html { get; set; } = "";
    public bool? Announcement { get; set; }
    public string? Graded { get; set; }
    public string? AssignmentUrl { get; set; }
    public List<Attachment>? Attachments { get; set; }
    public bool? Locked { get; set; }
    public bool? CanReply { get; set; }
    public bool? NeedFirst { get; set; }
    public string? LockText { get; set; }
    public List<Entry> Entries { get; set; } = new();
    public int? Count { get; set; }
    // (2.99.33) on Brightspace, the forum the topic is in (the author is then ""); "" on Canvas
    public string? Forum { get; set; }
}

public sealed class ModuleItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public int? Indent { get; set; }
    public string? Url { get; set; }
    public bool? File { get; set; }
    public bool? External { get; set; }
    public bool? Header { get; set; }
    public string? Requirement { get; set; }
    public bool? Done { get; set; }
    public bool? Markable { get; set; }
    public bool? Locked { get; set; }
    public string? LockText { get; set; }
    public string? Sub { get; set; }
    public string? ToolHost { get; set; }
}

public sealed class ModuleData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool? Locked { get; set; }
    public string? LockText { get; set; }
    public string? State { get; set; }
    public bool? Done { get; set; }
    public string? Progress { get; set; }
    public List<ModuleItem> Items { get; set; } = new();
}

public sealed class ModulesData
{
    public string Title { get; set; } = "";
    public string? Context { get; set; }
    public string? Color { get; set; }
    public List<ModuleData> Modules { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class ASection
{
    public string Title { get; set; } = "";
    public List<ARow> Rows { get; set; } = new();
}

public sealed class AssignmentsData
{
    public string Title { get; set; } = "";
    public string? Context { get; set; }
    public string? Color { get; set; }
    public List<ASection> Sections { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class GradeInfo
{
    public string Text { get; set; } = "";
    public double? Score { get; set; }
    public double? Possible { get; set; }
    public double? Pct { get; set; }
    public string? Letter { get; set; }
    public string? Late { get; set; }
    // (2.99.33) the figures as every app shows them: "81.3%", the ring's 81, the band (A–F) its colour comes from, and
    // the score and letter as one chip ("13/16 · B−")
    public string? PctText { get; set; }
    public int? PctWhole { get; set; }
    public string? Band { get; set; }
    public string? Chip { get; set; }
}

public sealed class RubricRating
{
    public string Text { get; set; } = "";
    public string? Pts { get; set; }
    public bool? Got { get; set; }
    public double? Value { get; set; }
    public string? Long { get; set; }
}

public sealed class RubricRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Pts { get; set; }
    public string? Comment { get; set; }
    public List<RubricRating> Ratings { get; set; } = new();
    public double? Worth { get; set; }
    public double? Score { get; set; }
    public string? Desc { get; set; }
    public string? Note { get; set; }
    // (2.99.33) the points given differ from the level picked; the mark's words ("Your mark · 4 pts", "Your mark", "")
    public bool? Adjusted { get; set; }
    public string? MarkText { get; set; }
}

public sealed class CommentRow
{
    public string Id { get; set; } = "";
    public string Author { get; set; } = "";
    public string? Avatar { get; set; }
    public string? When { get; set; }
    public string Text { get; set; } = "";
    public int? Attempt { get; set; }
    public List<Attachment>? Attachments { get; set; }
}

public sealed class SubmissionInfo
{
    public List<Attachment>? Files { get; set; }
    public string? Url { get; set; }
    public string? Text { get; set; }
    public string? Viewer { get; set; }
}

public sealed class ClassStats
{
    public double? Min { get; set; }
    public double? LowerQ { get; set; }
    public double? Median { get; set; }
    public double? UpperQ { get; set; }
    public double? Max { get; set; }
    public double? Mean { get; set; }
    public double? Mine { get; set; }
    public double? Possible { get; set; }
}

public sealed class AssignmentData
{
    public string Id { get; set; } = "";
    public string Course { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Context { get; set; }
    public string? Color { get; set; }
    public string? Kind { get; set; }
    public string? Points { get; set; }
    public string? Due { get; set; }
    // (2.99.33) "Due Oct 13 at 11:59 PM" ("" with no due date), and the facts in one line for a one-column page
    public string? DueLine { get; set; }
    public string? FactsLine { get; set; }
    public string? Available { get; set; }
    public string? TypesText { get; set; }
    public string Html { get; set; } = "";
    public Status? Status { get; set; }
    public GradeInfo? Grade { get; set; }
    public bool? Held { get; set; }
    public string? Stats { get; set; }
    public ClassStats? ClassStats { get; set; }
    public string? Submitted { get; set; }
    public string? AttemptsText { get; set; }
    public SubmissionInfo? Submission { get; set; }
    public List<string> Types { get; set; } = new();
    public List<string>? Allowed { get; set; }
    public bool CanSubmit { get; set; }
    public string? Why { get; set; }
    public bool? Resubmit { get; set; }
    public string? QuizUrl { get; set; }
    public string? QuizId { get; set; }
    public string? ToolUrl { get; set; }
    public string? ToolHost { get; set; }
    public bool? Alta { get; set; }
    public bool? Macmillan { get; set; }
    public bool? Aleks { get; set; }
    public bool? LtiQuiz { get; set; }
    public string? DiscussionUrl { get; set; }
    public string? CanvasUrl { get; set; }
    public List<CommentRow> Comments { get; set; } = new();
    public List<RubricRow> Rubric { get; set; } = new();
    public string? RubricTitle { get; set; }
    public string? RubricScore { get; set; }
}

public sealed class PageRow
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Sub { get; set; }
}

public sealed class PagesData
{
    public string Title { get; set; } = "";
    public string? Context { get; set; }
    public string? Color { get; set; }
    public List<PageRow> Rows { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class PageData
{
    public string Title { get; set; } = "";
    public string? Context { get; set; }
    public string? Color { get; set; }
    public string? Slug { get; set; }
    public string Html { get; set; } = "";
    public string? LockText { get; set; }
    public string? Edited { get; set; }
}

public sealed class FolderRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Sub { get; set; }
    public bool? Locked { get; set; }
}

public sealed class FileRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Sub { get; set; }
    public string? Url { get; set; }
    public string? Mime { get; set; }
    public string? Kind { get; set; }
    public bool? Locked { get; set; }
    // (2.99.33) what it is, in one vocabulary: PDF, Image, Spreadsheet, Slides, Document, Text, Video, Audio, Archive, Web page, File
    public string? TypeText { get; set; }
}

public sealed class FilesData
{
    public string Title { get; set; } = "";
    public string? Context { get; set; }
    public string? Color { get; set; }
    public List<FolderRow> Folders { get; set; } = new();
    public List<FileRow> Files { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class PersonRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Pronouns { get; set; }
    public string? Avatar { get; set; }
}

public sealed class PeopleSection
{
    public string Title { get; set; } = "";
    public List<PersonRow> Rows { get; set; } = new();
}

public sealed class PeopleData
{
    public string Title { get; set; } = "";
    public string? Context { get; set; }
    public string? Color { get; set; }
    public List<PeopleSection> Sections { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class RowsData
{
    public string Title { get; set; } = "";
    public string? Context { get; set; }
    public string? Color { get; set; }
    public List<ARow> Rows { get; set; } = new();
    public string? Empty { get; set; }
    public string? Html { get; set; }
}

public sealed class CGRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string GroupId { get; set; } = "";
    public double Possible { get; set; }
    public double? Earned { get; set; }
    public double? Effective { get; set; }
    public bool? Hypothetical { get; set; }
    public bool? Added { get; set; }
    public string? Badge { get; set; }
    public bool? Dropped { get; set; }
    public string? Grade { get; set; }
    public bool? Counted { get; set; }
    public string? DueText { get; set; }
    public string? Due { get; set; }
    public string? Url { get; set; }
    public string ScoreText { get; set; } = "";
}

public sealed class CGGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? WeightText { get; set; }
    public string? Value { get; set; }
    public double? Pct { get; set; }
    public string? Detail { get; set; }
    public string? Color { get; set; }
    public double? Weight { get; set; }
    public bool? Graded { get; set; }
}

public sealed class CourseGradesData
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    public string? Color { get; set; }
    public double? Total { get; set; }
    public string TotalText { get; set; } = "";
    public string? Letter { get; set; }
    public string? Note { get; set; }
    public string? Final { get; set; }
    public bool WhatIf { get; set; }
    public bool? Weighted { get; set; }
    public string? Target { get; set; }
    public List<ScaleStep> Scale { get; set; } = new();
    public List<CGGroup> Groups { get; set; } = new();
    public List<CGRow> Rows { get; set; } = new();
    public double? Gpa { get; set; }
    public double? GpaIf { get; set; }
    // (2.99.33) the course's other name when it says something new; how far Simpl's own reading is from the LMS's total
    // ("" when they agree); the term GPA now and with what-if ("3.43", "" when there is none); the overview's graded count
    public string? AltName { get; set; }
    public string? EstimateNote { get; set; }
    public string? GpaText { get; set; }
    public string? GpaIfText { get; set; }
    public int? Graded { get; set; }
    public int? TotalItems { get; set; }
    public string? GradedText { get; set; }
}

public sealed class GroupRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Sub { get; set; }
    public string? Color { get; set; }
    public bool? Past { get; set; }
    // (2.99.33) its course by name, and the course's code when it says something the name does not ("" else)
    public string? Course { get; set; }
    public string? CourseCode { get; set; }
}

public sealed class GroupsData
{
    public List<GroupRow> Current { get; set; } = new();
    public List<GroupRow> Past { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class ConvRow
{
    public string Id { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Who { get; set; } = "";
    public string? Preview { get; set; }
    public string? When { get; set; }
    public bool? Unread { get; set; }
    public bool? Starred { get; set; }
    public int? Count { get; set; }
    public string? Context { get; set; }
    public bool? Attachment { get; set; }
}

public sealed class InboxData
{
    public string Scope { get; set; } = "";
    public List<ConvRow> Rows { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class Message
{
    public string Id { get; set; } = "";
    public string Author { get; set; } = "";
    public string? Avatar { get; set; }
    public bool Mine { get; set; }
    public string? When { get; set; }
    public string Body { get; set; } = "";
    public List<Attachment>? Attachments { get; set; }
}

public sealed class ConversationData
{
    public string Id { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? Context { get; set; }
    public string? People { get; set; }
    public bool? Starred { get; set; }
    public List<Message> Messages { get; set; } = new();
}

public sealed class Recipient
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Sub { get; set; }
}

public sealed class RecipientsData
{
    public List<Recipient> Rows { get; set; } = new();
}

public sealed class ComposeContext
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Color { get; set; }
}

public sealed class ComposeContextsData
{
    public List<ComposeContext> Rows { get; set; } = new();
}

public sealed class DashLink
{
    public string? Kind { get; set; }
    public string? Label { get; set; }
    public string? Url { get; set; }
}

public sealed class DashNext
{
    public string Title { get; set; } = "";
    public string? When { get; set; }
    public string? Url { get; set; }
}

public sealed class DashCourse
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    public string? Sub { get; set; }
    public string? Color { get; set; }
    public string? Image { get; set; }
    public double? Score { get; set; }
    public string? ScoreText { get; set; }
    public string? Grade { get; set; }
    public int? Unread { get; set; }
    public int? DueToday { get; set; }
    public DashNext? Next { get; set; }
    public List<DashLink>? Links { get; set; }
    public string Url { get; set; } = "";
    public string? AltName { get; set; }
    public string? ScoreWhy { get; set; }
    public int? ScoreWhole { get; set; }
    public string? UnreadText { get; set; }
    public string? UnreadLabel { get; set; }
}

public sealed class DashCoursesData
{
    public List<DashCourse> Rows { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class DashRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Course { get; set; }
    public string? Color { get; set; }
    public string? Kind { get; set; }
    public string? Type { get; set; }
    public List<WorkFlag>? Flags { get; set; }
    public string? Points { get; set; }
    public string? Due { get; set; }
    public string? Time { get; set; }
    public bool Done { get; set; }
    public string? Url { get; set; }
    public bool? Custom { get; set; }
    // (2.99.107) its plan, as To Do's rows have it
    public WorkPlan? Plan { get; set; }
}

public sealed class DashDay
{
    public string Title { get; set; } = "";
    public string? Date { get; set; }
    public List<DashRow> Rows { get; set; } = new();
}

public sealed class DashListData
{
    public bool? HideDone { get; set; }
    public int? Done { get; set; }
    public int? Total { get; set; }
    public List<DashDay> Days { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class DashActivityRow
{
    public string Id { get; set; } = "";
    public string? Type { get; set; }
    public string? Kind { get; set; }
    public string Title { get; set; } = "";
    public string? Course { get; set; }
    public string? Color { get; set; }
    public string? When { get; set; }
    public string? Preview { get; set; }
    public string? Url { get; set; }
    public bool? Unread { get; set; }
}

public sealed class DashActivityData
{
    public List<DashActivityRow> Rows { get; set; } = new();
    public string? Empty { get; set; }
}

public sealed class DashSkyWindow
{
    public string? Band { get; set; }
    public bool? Free { get; set; }
    public string? Title { get; set; }
}

public sealed class DashSkyCourse
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public string? Color { get; set; }
    public double? Score { get; set; }
    public string? Url { get; set; }
    public List<DashSkyWindow>? Windows { get; set; }
    public string? ScoreText { get; set; }
    public int? ScoreWhole { get; set; }
}

public sealed class DashSkylineData
{
    public List<DashSkyCourse> Courses { get; set; } = new();
}
