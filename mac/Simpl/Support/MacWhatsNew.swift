import Foundation

/// What's New in Simpl for Mac (1.3.3): the Mac app's own notes, not the web extension's — what changed in each version
/// of this app, newest first. A note is a kind (new | improved | fixed), a title of two to five plain words (22
/// characters at most) and a short plain line (60 at most), as the web's notes are written (content/app/whatsnew-notes.js).
/// Every release adds its entry here; the newest must be the app's own version.
///
/// After an update, the versions since the one last seen are shown once (when the sheet is really up they are marked
/// seen); a first run sees none (its setup comes instead). Help ▸ What's New shows them all.
enum MacWhatsNew {
    static let releases: [Release] = [
        Release(version: "1.3.15", date: "2026-10-10", notes: [
            ReleaseNote(kind: "fixed", title: "Tidier action buttons", body: "Side-by-side buttons line up and never cut off words."),
            ReleaseNote(kind: "improved", title: "Simpler web windows", body: "The Cookies button is gone from web windows."),
        ]),
        Release(version: "1.3.14", date: "2026-10-10", notes: [
            ReleaseNote(kind: "improved", title: "Links open in Simpl", body: "Other sites open in a Simpl window, not your browser."),
            ReleaseNote(kind: "fixed", title: "Course files open", body: "A file Brightspace won't download opens in its viewer."),
        ]),
        Release(version: "1.3.13", date: "2026-10-10", notes: [
            ReleaseNote(kind: "new", title: "Move to Applications", body: "Opened from Downloads, Simpl offers to move itself."),
        ]),
        Release(version: "1.3.12", date: "2026-10-10", notes: [
            ReleaseNote(kind: "fixed", title: "Finished quizzes", body: "A Brightspace quiz you finished shows as done."),
            ReleaseNote(kind: "fixed", title: "Brightspace To Do", body: "To Do lists your Brightspace work again."),
            ReleaseNote(kind: "fixed", title: "Course files open", body: "A file Brightspace won't download opens in its viewer."),
        ]),
        Release(version: "1.3.11", date: "2026-10-09", notes: [
            ReleaseNote(kind: "fixed", title: "A tidier file viewer", body: "The viewer's empty toolbar row folds away."),
            ReleaseNote(kind: "fixed", title: "Box plot labels", body: "The class plot's labels are clear on any card."),
        ]),
        Release(version: "1.3.10", date: "2026-10-09", notes: [
            ReleaseNote(kind: "new", title: "How the class did", body: "A box plot of the class's scores, with yours on it."),
            ReleaseNote(kind: "improved", title: "Your work, then again", body: "See Feedback or View Submission beside Hand In Again."),
            ReleaseNote(kind: "improved", title: "A Mac-like viewer", body: "Canvas's file viewer wears the Mac's look in Feedback."),
        ]),
        Release(version: "1.3.9", date: "2026-10-09", notes: [
            ReleaseNote(kind: "fixed", title: "Complete grades", body: "A complete grade shows a tick in its ring, not a word."),
            ReleaseNote(kind: "fixed", title: "Quiz drop-downs", body: "An unanswered drop-down says Select, never an answer."),
            ReleaseNote(kind: "improved", title: "Results as tiles", body: "A quiz's results show their questions as tiles too."),
        ]),
        Release(version: "1.3.8", date: "2026-10-09", notes: [
            ReleaseNote(kind: "improved", title: "Locked work opens", body: "Open locked items to see why and when they open."),
            ReleaseNote(kind: "new", title: "Feedback in a popup", body: "See your file with your teacher's marks and comments."),
            ReleaseNote(kind: "new", title: "Your school on top", body: "Your school's logo and name head the sidebar."),
            ReleaseNote(kind: "improved", title: "Side column stays put", body: "An assignment's side column stays as you scroll."),
            ReleaseNote(kind: "improved", title: "Question numbers", body: "A neat card of numbered tiles; a click goes there."),
            ReleaseNote(kind: "improved", title: "Preview opens itself", body: "A file added to Hand In shows its preview at once."),
            ReleaseNote(kind: "fixed", title: "Tool work isn't closed", body: "A tool's assignment no longer says it is closed."),
        ]),
        Release(version: "1.3.7", date: "2026-10-09", notes: [
            ReleaseNote(kind: "new", title: "Previous and Next", body: "Step through a course's work, pages and posts."),
            ReleaseNote(kind: "improved", title: "A roomier quiz", body: "Quizzes open wide, with more space round each answer."),
            ReleaseNote(kind: "fixed", title: "Question numbers", body: "One click on a number goes to that question."),
        ]),
        Release(version: "1.3.6", date: "2026-10-09", notes: [
            ReleaseNote(kind: "fixed", title: "Module check marks", body: "A module is ticked only once all its work is done."),
        ]),
        Release(version: "1.3.5", date: "2026-10-09", notes: [
            ReleaseNote(kind: "improved", title: "Quizzes you've taken", body: "See Feedback, and New Attempt while one is left."),
            ReleaseNote(kind: "improved", title: "Elements in the pin", body: "Click an element to see its card, right in the pin."),
        ]),
        Release(version: "1.3.4", date: "2026-10-09", notes: [
            ReleaseNote(kind: "improved", title: "A tour to the end", body: "The tour runs to its last step, as on the web."),
        ]),
        Release(version: "1.3.3", date: "2026-10-09", notes: [
            ReleaseNote(kind: "new", title: "Notes for the Mac", body: "What's New lists what changed in this app alone."),
            ReleaseNote(kind: "improved", title: "Inked photos", body: "Your photos are drawn in two soft tones, as on the web."),
        ]),
        Release(version: "1.3.2", date: "2026-10-09", notes: [
            ReleaseNote(kind: "new", title: "Refresh button", body: "Reload any screen from the toolbar, or press ⌘R."),
            ReleaseNote(kind: "improved", title: "Colour picker redone", body: "The custom colour wheel opens right in Settings."),
            ReleaseNote(kind: "improved", title: "Preview in Hand In", body: "A file's preview opens beside the form, in the sheet."),
        ]),
        Release(version: "1.3.1", date: "2026-10-09", notes: [
            ReleaseNote(kind: "new", title: "Your own photos", body: "Put a photo behind the sidebar and each Dashboard card."),
            ReleaseNote(kind: "new", title: "Move and zoom photos", body: "Drag a photo to place it; pinch or slide to zoom."),
            ReleaseNote(kind: "new", title: "Any colour you like", body: "Pick a custom colour on the colour wheel."),
            ReleaseNote(kind: "improved", title: "Colour everywhere", body: "The sidebar, card icons and titles take your colour."),
            ReleaseNote(kind: "improved", title: "A cleaner Dashboard", body: "The Courses card is gone from the List view."),
            ReleaseNote(kind: "fixed", title: "Sidebar after themes", body: "Changing the theme no longer moves the sidebar."),
        ]),
        Release(version: "1.3", date: "2026-10-09", notes: [
            ReleaseNote(kind: "new", title: "Themes for the Mac", body: "Settings ▸ Appearance: the web's themes, on the Mac."),
            ReleaseNote(kind: "new", title: "Nicknames step", body: "Setup asks for course nicknames on a page of its own."),
            ReleaseNote(kind: "new", title: "Preview before Hand In", body: "Click a chosen file to see it before you submit."),
            ReleaseNote(kind: "improved", title: "Resize the window", body: "Screens fit a narrow window; the sidebar folds away."),
            ReleaseNote(kind: "improved", title: "Show Completed", body: "To Do's completed work, shown or hidden in one click."),
            ReleaseNote(kind: "improved", title: "Drop box opens Finder", body: "Click Hand In's drop area to choose files."),
        ]),
        Release(version: "1.2.20", date: "2026-10-09", notes: [
            ReleaseNote(kind: "improved", title: "Previews under rows", body: "A quick look opens just under the row you clicked."),
        ]),
        Release(version: "1.2.19", date: "2026-10-09", notes: [
            ReleaseNote(kind: "improved", title: "Hourly updates", body: "Updates install within the hour, never mid-quiz."),
        ]),
        Release(version: "1.2.18", date: "2026-10-09", notes: [
            ReleaseNote(kind: "fixed", title: "Overdue count", body: "Dismissing overdue work lowers the count at once."),
        ]),
        Release(version: "1.2.17", date: "2026-10-09", notes: [
            ReleaseNote(kind: "new", title: "Your grading scale", body: "Set each letter's cutoff to match your instructor."),
        ]),
        Release(version: "1.2.16", date: "2026-10-09", notes: [
            ReleaseNote(kind: "improved", title: "Tool windows", body: "Pinned tools and a dark page in external tools."),
            ReleaseNote(kind: "improved", title: "Smoother previews", body: "Previews open in place, with a clear Close."),
            ReleaseNote(kind: "improved", title: "Lighter on your Mac", body: "Simpl uses less memory and power in the background."),
        ]),
        Release(version: "1.2.12", date: "2026-10-09", notes: [
            ReleaseNote(kind: "improved", title: "Faster card numbers", body: "Overdue and Graded show up right away."),
        ]),
        Release(version: "1.2.11", date: "2026-10-09", notes: [
            ReleaseNote(kind: "new", title: "Dismiss overdue work", body: "Dismiss several overdue items in a row."),
        ]),
        Release(version: "1.2.10", date: "2026-10-09", notes: [
            ReleaseNote(kind: "improved", title: "Work opens in course", body: "An assignment opens inside its course."),
        ]),
        Release(version: "1.2.6", date: "2026-10-09", notes: [
            ReleaseNote(kind: "new", title: "Check your school", body: "See your school's sign-in page before you sign in."),
        ]),
        Release(version: "1.2.5", date: "2026-10-09", notes: [
            ReleaseNote(kind: "new", title: "Brightspace schools", body: "Find Brightspace schools in the school search."),
        ]),
        Release(version: "1.2.4", date: "2026-10-09", notes: [
            ReleaseNote(kind: "improved", title: "Reset erases all", body: "Reset signs out, erases your data and restarts Simpl."),
        ]),
        Release(version: "1.2.3", date: "2026-10-09", notes: [
            ReleaseNote(kind: "new", title: "Rubric ring", body: "Rubrics open on the ring, as on the web."),
            ReleaseNote(kind: "new", title: "Quick looks", body: "Click a row to preview the work without opening it."),
        ]),
        Release(version: "1.2.1", date: "2026-10-08", notes: [
            ReleaseNote(kind: "improved", title: "A new tour", body: "A short tour of Simpl the first time you open it."),
        ]),
        Release(version: "1.2", date: "2026-10-08", notes: [
            ReleaseNote(kind: "new", title: "Updates by themselves", body: "Simpl keeps itself up to date."),
            ReleaseNote(kind: "improved", title: "Opens faster", body: "Screens open on what they showed last time."),
        ]),
    ]

    private static let seenKey = "macWhatsNewSeen"
    static var current: String { (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String) ?? "0" }

    /// Every version's notes (Help ▸ What's New).
    static var all: WhatsNewData { WhatsNewData(version: current, releases: releases) }

    /// After an update, the versions since the one last seen (nil: nothing to show). Never seen before: this
    /// version's own notes alone — an update from a version that showed the web's notes.
    static func due() -> WhatsNewData? {
        let seen = UserDefaults.standard.string(forKey: seenKey)
        let newer = releases.filter { r in
            seen.map { Updater.isNewer(r.version, than: $0) } ?? (r.version == current)
        }.filter { !Updater.isNewer($0.version, than: current) }
        return newer.isEmpty ? nil : WhatsNewData(version: current, releases: newer)
    }

    /// This version's notes seen (the sheet was up, or the first run's setup made them moot).
    static func markSeen() {
        UserDefaults.standard.set(current, forKey: seenKey)
    }
}
