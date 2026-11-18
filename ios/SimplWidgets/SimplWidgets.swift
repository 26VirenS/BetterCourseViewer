import SwiftUI
import WidgetKit

/// (iPhone 1.9) Simpl Courses' widgets, for the Home Screen and the Lock Screen: Up Next (the work due next) and Grades
/// (the term GPA and each course's score). Each draws the feed the app keeps for them (WidgetFeed, in the container the
/// two share) and opens the app on what it shows when pressed.
@main
struct SimplCoursesWidgets: WidgetBundle {
    var body: some Widget {
        UpNextWidget()
        GradesWidget()
    }
}
