import SwiftUI

// (temporary: screens still being written, so the rest of the app builds meanwhile)

struct GradesView: View { var body: some View { Text("Grades") } }
struct CourseGradesView: View { let courseId: String; var body: some View { Text("Course grades \(courseId)") } }
struct InboxView: View { var body: some View { Text("Inbox") } }
struct ConversationView: View { let id: String; var body: some View { Text("Conversation \(id)") } }
struct ComposeSheet: View {
    var to: [Recipient] = []
    var context: String? = nil
    var sent: () -> Void = {}
    var body: some View { Text("Compose") }
}
struct GroupsView: View { var body: some View { Text("Groups") } }
struct TodoView: View { var body: some View { Text("To Do") } }
struct NewTaskSheet: View { var body: some View { Text("New Task") } }
struct CalendarView: View { var body: some View { Text("Calendar") } }
struct AssignmentView: View { let course: String; let id: String; var body: some View { Text("Assignment \(id)") } }
struct TopicView: View { let ctx: String; let id: String; var body: some View { Text("Topic \(id)") } }
struct SetupScreen: View { var body: some View { Text("Setup") } }
struct QuizScreen: View { let launch: QuizLaunch; var body: some View { Text("Quiz \(launch.quiz)") } }
