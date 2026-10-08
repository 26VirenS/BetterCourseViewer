import Foundation

// What the page's engine answers for a course, a group and the Inbox (native-app.js, 1.2). As in
// Models.swift, anything the screens can do without is optional, so a page a version apart decodes.

struct Status: Codable, Hashable {
    var word: String
    var kind: String?
}

/// A piece of work in a list (an assignment, a quiz, a dated item of the syllabus).
struct ARow: Codable, Identifiable, Hashable {
    var id: String
    var title: String
    var kind: String?
    var sub: String?
    var status: Status?
    var url: String
}

struct SectionLink: Codable, Identifiable, Hashable {
    var kind: String
    var label: String
    var id: String { kind }
}

struct MoreLink: Codable, Identifiable, Hashable {
    var label: String
    var url: String
    var external: Bool?
    var tool: String?
    var id: String { url }
}

/// An announcement or a discussion in a list.
struct PostRow: Codable, Identifiable, Hashable {
    var id: String
    var title: String
    var author: String?
    var avatar: String?
    var when: String?
    var preview: String?
    var unread: Bool?
    var unreadCount: Int?
    var replies: Int?
    var graded: Bool?
    var url: String
}

struct FrontPage: Codable, Hashable {
    var title: String
    var excerpt: String?
    var slug: String?
}

struct HomeData: Codable {
    var ctx: String
    var kind: String
    var title: String
    var name: String?
    var sub: String?
    var color: String?
    var teachers: String?
    var score: Double?
    var scoreText: String?
    var letter: String?
    var sections: [SectionLink]
    var more: [MoreLink]?
    var open: [ARow]
    var openCount: Int?
    var done: [ARow]?
    var announcements: [PostRow]
    var front: FrontPage?
    var html: String?
}

struct PostListData: Codable {
    var title: String
    var context: String?
    var color: String?
    var rows: [PostRow]
    var empty: String?
}

struct PostSection: Codable, Identifiable, Hashable {
    var title: String
    var rows: [PostRow]
    var id: String { title }
}

struct DiscussionsData: Codable {
    var title: String
    var context: String?
    var color: String?
    var sections: [PostSection]
    var empty: String?
}

struct Attachment: Codable, Identifiable, Hashable {
    var name: String
    var url: String
    var id: String { url }
}

/// A reply in a discussion, in reading order, with how deep in its thread it sits.
struct Entry: Codable, Identifiable, Hashable {
    var id: String
    var author: String
    var avatar: String?
    var when: String?
    var text: String
    var html: String?
    var rich: Bool?
    var depth: Int
    var parent: String?
    var deleted: Bool?
}

struct TopicData: Codable {
    var id: String
    var title: String
    var context: String?
    var color: String?
    var author: String?
    var avatar: String?
    var when: String?
    var html: String
    var announcement: Bool?
    var graded: String?
    var assignmentUrl: String?
    var attachments: [Attachment]?
    var locked: Bool?
    var canReply: Bool?
    var needFirst: Bool?
    var lockText: String?
    var entries: [Entry]
    var count: Int?
}

struct ModuleItem: Codable, Identifiable, Hashable {
    var id: String
    var title: String
    var type: String
    var indent: Int?
    var url: String?
    var file: Bool?
    var external: Bool?
    var header: Bool?
    var requirement: String?
    var done: Bool?
    var markable: Bool?
    var locked: Bool?
    var lockText: String?
    var sub: String?
}

struct ModuleData: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var locked: Bool?
    var lockText: String?
    var state: String?
    var done: Bool?
    var progress: String?
    var items: [ModuleItem]
}

struct ModulesData: Codable {
    var title: String
    var context: String?
    var color: String?
    var modules: [ModuleData]
    var empty: String?
}

struct ASection: Codable, Identifiable, Hashable {
    var title: String
    var rows: [ARow]
    var id: String { title }
}

struct AssignmentsData: Codable {
    var title: String
    var context: String?
    var color: String?
    var sections: [ASection]
    var empty: String?
}

struct GradeInfo: Codable, Hashable {
    var text: String
    var score: Double?
    var possible: Double?
    var pct: Double?
    var letter: String?
    var late: String?
}

struct RubricRating: Codable, Hashable {
    var text: String
    var pts: String?
    var got: Bool?
    /// The level's points as a number, and its own longer description (the Mac's rubric ring).
    var value: Double?
    var long: String?
}

struct RubricRow: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var pts: String?
    var comment: String?
    var ratings: [RubricRating]
    /// What the criterion is worth and was given, its long description, and the marker's note as written (the Mac's
    /// rubric ring).
    var worth: Double?
    var score: Double?
    var desc: String?
    var note: String?
}

struct CommentRow: Codable, Identifiable, Hashable {
    var id: String
    var author: String
    var avatar: String?
    var when: String?
    var text: String
    var attempt: Int?
    var attachments: [Attachment]?
}

struct SubmissionInfo: Codable, Hashable {
    var files: [Attachment]?
    var url: String?
    var text: String?
}

struct AssignmentData: Codable {
    var id: String
    var course: String
    var title: String
    var context: String?
    var color: String?
    var kind: String?
    var points: String?
    var due: String?
    var available: String?
    var typesText: String?
    var html: String
    var status: Status?
    var grade: GradeInfo?
    var held: Bool?
    var stats: String?
    var submitted: String?
    var attemptsText: String?
    var submission: SubmissionInfo?
    var types: [String]
    var allowed: [String]?
    var canSubmit: Bool
    var why: String?
    var resubmit: Bool?
    var quizUrl: String?
    var quizId: String?
    var toolUrl: String?
    var ltiQuiz: Bool?
    var discussionUrl: String?
    var canvasUrl: String?
    var comments: [CommentRow]
    var rubric: [RubricRow]
    var rubricTitle: String?
    var rubricScore: String?
}

struct PageRow: Codable, Identifiable, Hashable {
    var slug: String
    var title: String
    var sub: String?
    var id: String { slug }
}

struct PagesData: Codable {
    var title: String
    var context: String?
    var color: String?
    var rows: [PageRow]
    var empty: String?
}

struct PageData: Codable {
    var title: String
    var context: String?
    var color: String?
    var slug: String?
    var html: String
    var lockText: String?
    var edited: String?
}

struct FolderRow: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var sub: String?
    var locked: Bool?
}

struct FileRow: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var sub: String?
    var url: String?
    var mime: String?
    var kind: String?
    var locked: Bool?
}

struct FilesData: Codable {
    var title: String
    var context: String?
    var color: String?
    var folders: [FolderRow]
    var files: [FileRow]
    var empty: String?
}

struct PersonRow: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var pronouns: String?
    var avatar: String?
}

struct PeopleSection: Codable, Identifiable, Hashable {
    var title: String
    var rows: [PersonRow]
    var id: String { title }
}

struct PeopleData: Codable {
    var title: String
    var context: String?
    var color: String?
    var sections: [PeopleSection]
    var empty: String?
}

/// A list of work with an optional text above it (Quizzes; the Syllabus with its dated work).
struct RowsData: Codable {
    var title: String
    var context: String?
    var color: String?
    var rows: [ARow]
    var empty: String?
    var html: String?
}

struct CGRow: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var groupId: String
    var possible: Double
    var earned: Double?
    var effective: Double?
    var hypothetical: Bool?
    var added: Bool?
    var badge: String?
    var dropped: Bool?
    var grade: String?
    var counted: Bool?
    var dueText: String?
    var due: String?
    var url: String?
    var scoreText: String
}

struct CGGroup: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var weightText: String?
    var value: String?
    var pct: Double?
    var detail: String?
    var color: String?
}

struct CourseGradesData: Codable {
    var id: String
    var code: String
    var name: String?
    var color: String?
    var total: Double?
    var totalText: String
    var letter: String?
    var note: String?
    var final: String?
    var whatIf: Bool
    var weighted: Bool?
    var target: String?
    var scale: [ScaleStep]
    var groups: [CGGroup]
    var rows: [CGRow]
    var gpa: Double?
    var gpaIf: Double?
}

struct GroupRow: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var sub: String?
    var color: String?
    var past: Bool?
}

struct GroupsData: Codable {
    var current: [GroupRow]
    var past: [GroupRow]
    var empty: String?
}

struct ConvRow: Codable, Identifiable, Hashable {
    var id: String
    var subject: String
    var who: String
    var preview: String?
    var when: String?
    var unread: Bool?
    var starred: Bool?
    var count: Int?
    var context: String?
    var attachment: Bool?
}

struct InboxData: Codable {
    var scope: String
    var rows: [ConvRow]
    var empty: String?
}

struct Message: Codable, Identifiable, Hashable {
    var id: String
    var author: String
    var avatar: String?
    var mine: Bool
    var when: String?
    var body: String
    var attachments: [Attachment]?
}

struct ConversationData: Codable {
    var id: String
    var subject: String
    var context: String?
    var people: String?
    var starred: Bool?
    var messages: [Message]
}

struct Recipient: Codable, Identifiable, Hashable {
    var id: String
    var name: String
    var sub: String?
}

struct RecipientsData: Codable {
    var rows: [Recipient]
}

struct ComposeContext: Codable, Identifiable, Hashable {
    var code: String
    var name: String
    var color: String?
    var id: String { code }
}

struct ComposeContextsData: Codable {
    var rows: [ComposeContext]
}
