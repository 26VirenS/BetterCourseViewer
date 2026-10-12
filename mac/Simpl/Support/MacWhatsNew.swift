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
        Release(version: "1.3.74", date: "2026-10-12", notes: [
            ReleaseNote(kind: "fixed", title: "True molecule shapes", body: "CO₂ is straight and water bends to 104.5° in the 3D builder."),
            ReleaseNote(kind: "fixed", title: "Polarity put right", body: "CO₂, BF₃, SF₆, XeF₄ and nitrate now show as nonpolar."),
        ]),
        Release(version: "1.3.73", date: "2026-10-12", notes: [
            ReleaseNote(kind: "new", title: "Tools over popups", body: "Your pinned tools sit on a quiz or Alta popup's own bar."),
            ReleaseNote(kind: "new", title: "Pull a tool out", body: "Drag a pin's panel by its grip: it floats where you put it."),
            ReleaseNote(kind: "improved", title: "Files to Downloads", body: "A file you open or download lands in your Downloads folder."),
        ]),
        Release(version: "1.3.72", date: "2026-10-11", notes: [
            ReleaseNote(kind: "improved", title: "Updates you can trust", body: "Critical fixes now reach every Mac as soon as they're out."),
        ]),
        Release(version: "1.3.71", date: "2026-10-11", notes: [
            ReleaseNote(kind: "improved", title: "For everyone now", body: "Everything from the 1.3.66–1.3.70 betas, now for everyone."),
        ]),
        Release(version: "1.3.70", date: "2026-10-11", notes: [
            ReleaseNote(kind: "improved", title: "Graphs, small to start", body: "Click a graph to open it large in a window you can move."),
            ReleaseNote(kind: "improved", title: "Dark graphs", body: "Graphs are dark in dark mode; the sun button lightens them."),
            ReleaseNote(kind: "fixed", title: "No more Alta flashes", body: "Alta's loading screens stay hidden behind the skeleton."),
            ReleaseNote(kind: "improved", title: "Softer button haze", body: "The glow behind the quiz buttons no longer has edges."),
        ]),
        Release(version: "1.3.69", date: "2026-10-11", notes: [
            ReleaseNote(kind: "new", title: "More than one window", body: "File ▸ New Window, or right-click a sidebar item."),
            ReleaseNote(kind: "new", title: "Grade breakdown bar", body: "Weighted courses show each group's share of your grade."),
        ]),
        Release(version: "1.3.68", date: "2026-10-11", notes: [
            ReleaseNote(kind: "fixed", title: "Readable Alta graphs", body: "In dark mode, graphs sit on a white card of their own."),
            ReleaseNote(kind: "improved", title: "Loading, not flashing", body: "A skeleton shows until Alta's question is fully drawn."),
        ]),
        Release(version: "1.3.67", date: "2026-10-11", notes: [
            ReleaseNote(kind: "new", title: "External Tools", body: "Every tool your courses use, in one list in the sidebar."),
        ]),
        Release(version: "1.3.66", date: "2026-10-11", notes: [
            ReleaseNote(kind: "new", title: "Macmillan Learning", body: "Macmillan assignments open in their own popup."),
        ]),
        Release(version: "1.3.65", date: "2026-10-11", notes: [
            ReleaseNote(kind: "improved", title: "A faster table", body: "The table, its builders and its pages do far less work."),
        ]),
        Release(version: "1.3.64", date: "2026-10-11", notes: [
            ReleaseNote(kind: "new", title: "Search results", body: "The table's search lists what it finds; click to open."),
            ReleaseNote(kind: "improved", title: "A calmer tour", body: "Each step is ticked off, with a pause before the next."),
        ]),
        Release(version: "1.3.63", date: "2026-10-11", notes: [
            ReleaseNote(kind: "fixed", title: "Minus buttons work", body: "The Lewis builder's − buttons take every press."),
        ]),
        Release(version: "1.3.62", date: "2026-10-11", notes: [
            ReleaseNote(kind: "new", title: "Interactive tour", body: "Each step asks for one thing and moves on once done."),
            ReleaseNote(kind: "improved", title: "Search in the header", body: "Pages and a glass search button sit at the top."),
            ReleaseNote(kind: "improved", title: "Tidier dashboard cards", body: "Narrower when open; a click anywhere closes them."),
            ReleaseNote(kind: "fixed", title: "Graphs drawn properly", body: "Alta's graphs keep their own colours in dark mode."),
            ReleaseNote(kind: "fixed", title: "Smoother Alta loading", body: "Alta stays out of sight between questions until ready."),
        ]),
        Release(version: "1.3.61", date: "2026-10-11", notes: [
            ReleaseNote(kind: "improved", title: "A real guided tour", body: "The tour lights each control it explains, step by step."),
        ]),
        Release(version: "1.3.60", date: "2026-10-11", notes: [
            ReleaseNote(kind: "fixed", title: "No more flashing", body: "Alta's page stays hidden until its question is ready."),
            ReleaseNote(kind: "improved", title: "Narrower questions", body: "Alta's question and answer sit in a centred column."),
            ReleaseNote(kind: "fixed", title: "Every objective named", body: "The rail lists every objective from the start screen."),
        ]),
        Release(version: "1.3.59", date: "2026-10-11", notes: [
            ReleaseNote(kind: "new", title: "Alta start screen", body: "Objectives and a clear Start, on Liquid Glass."),
            ReleaseNote(kind: "new", title: "A quick tour", body: "A short tour of the table and its builders."),
            ReleaseNote(kind: "improved", title: "Spin control", body: "Turn on or off at the viewer's corner; pauses while held."),
            ReleaseNote(kind: "fixed", title: "Cleaner 3D models", body: "A clean selection ring; labels hide behind nearer atoms."),
            ReleaseNote(kind: "fixed", title: "Even Alta panels", body: "Alta's question and answer panels share one width."),
        ]),
        Release(version: "1.3.58", date: "2026-10-11", notes: [
            ReleaseNote(kind: "fixed", title: "Tidy builder controls", body: "The builders' left column fits its space, nothing cut off."),
            ReleaseNote(kind: "improved", title: "3D models turn", body: "A 3D molecule turns slowly from the start."),
            ReleaseNote(kind: "improved", title: "Atoms in two columns", body: "The atoms round the centre in a compact grid."),
        ]),
        Release(version: "1.3.57", date: "2026-10-11", notes: [
            ReleaseNote(kind: "fixed", title: "Even element facts", body: "Every fact box one size; the atom's card its own height."),
            ReleaseNote(kind: "fixed", title: "Orbitals in view", body: "Orbitals fit the view and open on the subshell filling."),
            ReleaseNote(kind: "improved", title: "Lewis drawings sized", body: "Small molecules drawn larger, shaped as on paper."),
        ]),
        Release(version: "1.3.56", date: "2026-10-11", notes: [
            ReleaseNote(kind: "fixed", title: "Tidier element pages", body: "The atom and its electrons side by side, facts below."),
        ]),
        Release(version: "1.3.55", date: "2026-10-11", notes: [
            ReleaseNote(kind: "improved", title: "Presets in a menu", body: "Lewis and 3D presets sit in one tidy grouped menu."),
            ReleaseNote(kind: "improved", title: "Easier atom picking", body: "Pick the central atom from a menu, group by group."),
            ReleaseNote(kind: "fixed", title: "Readable ion charges", body: "An ion's charge, like Na⁺'s, is drawn full size."),
        ]),
        Release(version: "1.3.54", date: "2026-10-11", notes: [
            ReleaseNote(kind: "new", title: "Draw any molecule", body: "Chains, rings and pairs: draw it, the bonds sort out."),
            ReleaseNote(kind: "new", title: "Resonance and radicals", body: "Every resonance structure, odd electrons, ions."),
            ReleaseNote(kind: "improved", title: "Glass table controls", body: "Kinds and colour views as glass; trends always on."),
            ReleaseNote(kind: "fixed", title: "Cleaner Alta pop-ups", body: "An Alta pop-up's picture leaves out its buttons."),
        ]),
        Release(version: "1.3.53", date: "2026-10-11", notes: [
            ReleaseNote(kind: "improved", title: "Question and answer", body: "Alta's question and answer on glass panels of their own."),
            ReleaseNote(kind: "improved", title: "Every Alta button", body: "Alta's buttons, whatever they say, as the app's own."),
        ]),
        Release(version: "1.3.52", date: "2026-10-11", notes: [
            ReleaseNote(kind: "new", title: "Colour the table by…", body: "Electronegativity, radius and more, with trends."),
            ReleaseNote(kind: "new", title: "Element pages", body: "Press an element: its 3D atom, orbitals and facts."),
            ReleaseNote(kind: "new", title: "Lewis structures", body: "Pick atoms; see bonds, lone pairs and its shape."),
            ReleaseNote(kind: "new", title: "3D molecule builder", body: "Build a molecule and turn it; VSEPR shapes it."),
        ]),
        Release(version: "1.3.51", date: "2026-10-11", notes: [
            ReleaseNote(kind: "improved", title: "Alta pop-ups, on the Mac", body: "Alta's pop-ups open as Mac sheets, drawn whole."),
        ]),
        Release(version: "1.3.50", date: "2026-10-11", notes: [
            ReleaseNote(kind: "improved", title: "Dark glass for Alta", body: "In dark mode, Alta's question is light on dark glass."),
        ]),
        Release(version: "1.3.49", date: "2026-10-11", notes: [
            ReleaseNote(kind: "new", title: "Questions on glass", body: "Alta's own question, laid on Liquid Glass."),
        ]),
        Release(version: "1.3.48", date: "2026-10-11", notes: [
            ReleaseNote(kind: "fixed", title: "Lessons stay focused", body: "More Instruction's lesson shows without Alta's header."),
        ]),
        Release(version: "1.3.47", date: "2026-10-11", notes: [
            ReleaseNote(kind: "new", title: "Just the question", body: "Alta shows only its question, else its page as is."),
            ReleaseNote(kind: "new", title: "Error Logs", body: "When Alta's page isn't recognised, see why."),
        ]),
        Release(version: "1.3.46", date: "2026-10-11", notes: [
            ReleaseNote(kind: "new", title: "Save Page Code", body: "Save all of Alta's page code to a text file."),
        ]),
        Release(version: "1.3.45", date: "2026-10-11", notes: [
            ReleaseNote(kind: "improved", title: "Alta, as Alta draws it", body: "Alta's page as it is, with objectives and mastery."),
        ]),
        Release(version: "1.3.44", date: "2026-10-11", notes: [
            ReleaseNote(kind: "fixed", title: "Try Again stays put", body: "A wrong answer offers Try Again, not Alta's page."),
        ]),
        Release(version: "1.3.43", date: "2026-10-11", notes: [
            ReleaseNote(kind: "new", title: "Questions on glass", body: "Alta's questions sit on Liquid Glass."),
        ]),
        Release(version: "1.3.42", date: "2026-10-11", notes: [
            ReleaseNote(kind: "fixed", title: "Alta's top stays away", body: "Alta's title and objective card stay hidden."),
        ]),
        Release(version: "1.3.41", date: "2026-10-11", notes: [
            ReleaseNote(kind: "fixed", title: "Alta's top stays away", body: "Alta's title and objective card stay hidden."),
        ]),
        Release(version: "1.3.40", date: "2026-10-11", notes: [
            ReleaseNote(kind: "fixed", title: "No gap above questions", body: "Alta's question sits at the top, no empty band."),
        ]),
        Release(version: "1.3.39", date: "2026-10-11", notes: [
            ReleaseNote(kind: "fixed", title: "Right on a retry", body: "A right second attempt shows as right."),
        ]),
        Release(version: "1.3.38", date: "2026-10-11", notes: [
            ReleaseNote(kind: "fixed", title: "Alta's question, in full", body: "Alta's question card shows whole, every time."),
        ]),
        Release(version: "1.3.37", date: "2026-10-10", notes: [
            ReleaseNote(kind: "improved", title: "Lesson, then question", body: "A lesson shows with its question right under it."),
            ReleaseNote(kind: "improved", title: "Alta's notices", body: "Alta's notices show briefly, then go away."),
        ]),
        Release(version: "1.3.36", date: "2026-10-10", notes: [
            ReleaseNote(kind: "fixed", title: "Videos in Alta lessons", body: "Lesson videos show as Alta draws them."),
            ReleaseNote(kind: "fixed", title: "No Check on lessons", body: "A lesson's bar offers Continue, never Check."),
        ]),
        Release(version: "1.3.35", date: "2026-10-10", notes: [
            ReleaseNote(kind: "fixed", title: "View Instruction stays", body: "Alta's lesson stays on screen until you go back."),
        ]),
        Release(version: "1.3.34", date: "2026-10-10", notes: [
            ReleaseNote(kind: "fixed", title: "Question after a lesson", body: "After a lesson, the question shows in full."),
        ]),
        Release(version: "1.3.33", date: "2026-10-10", notes: [
            ReleaseNote(kind: "fixed", title: "View Instruction", body: "View Instruction shows the lesson, and stays."),
        ]),
        Release(version: "1.3.32", date: "2026-10-10", notes: [
            ReleaseNote(kind: "new", title: "Every quiz attempt", body: "A quiz lists each attempt with its score."),
            ReleaseNote(kind: "fixed", title: "Quiz grades on cards", body: "A graded quiz's card shows its score everywhere."),
        ]),
        Release(version: "1.3.31", date: "2026-10-10", notes: [
            ReleaseNote(kind: "improved", title: "Lesson, then question", body: "After a lesson, its question shows on its own."),
            ReleaseNote(kind: "new", title: "View Instruction", body: "See the lesson again from the bar, then go back."),
        ]),
        Release(version: "1.3.30", date: "2026-10-10", notes: [
            ReleaseNote(kind: "fixed", title: "Alta lesson maths", body: "Lesson maths loads quickly and sets out right."),
            ReleaseNote(kind: "fixed", title: "Continue on lessons", body: "Lessons show Continue, ready once you've read it."),
        ]),
        Release(version: "1.3.29", date: "2026-10-10", notes: [
            ReleaseNote(kind: "fixed", title: "Maths in Alta lessons", body: "Alta lessons' maths shows as it should."),
            ReleaseNote(kind: "fixed", title: "Continue in lessons", body: "Lessons offer Continue, not a greyed-out Check."),
        ]),
        Release(version: "1.3.28", date: "2026-10-10", notes: [
            ReleaseNote(kind: "new", title: "Alta lessons in place", body: "More Instruction's lessons show in the popup."),
            ReleaseNote(kind: "fixed", title: "Steadier card previews", body: "Card previews no longer jump as they update."),
        ]),
        Release(version: "1.3.26", date: "2026-10-10", notes: [
            ReleaseNote(kind: "fixed", title: "Alta's pop-ups", body: "Alta's Having trouble? pop-up shows clearly."),
        ]),
        Release(version: "1.3.25", date: "2026-10-10", notes: [
            ReleaseNote(kind: "fixed", title: "Alta scrolls freely", body: "Alta's question no longer jumps back to the top."),
            ReleaseNote(kind: "fixed", title: "Alta's maths keypad", body: "Alta's keypad looks as Alta draws it, in dark mode."),
            ReleaseNote(kind: "improved", title: "More Instruction", body: "More Instruction sits beside Check, bottom right."),
        ]),
        Release(version: "1.3.23", date: "2026-10-10", notes: [
            ReleaseNote(kind: "new", title: "Alta's own answer box", body: "Answer in Alta's own box, in Simpl's colours."),
            ReleaseNote(kind: "fixed", title: "Alta objective names", body: "Objective names and estimates match Alta's."),
        ]),
        Release(version: "1.3.22", date: "2026-10-10", notes: [
            ReleaseNote(kind: "fixed", title: "Alta questions in full", body: "Alta's own questions show their words and Check."),
            ReleaseNote(kind: "fixed", title: "More drawn questions", body: "More maths questions are drawn by the popup."),
        ]),
        Release(version: "1.3.21", date: "2026-10-10", notes: [
            ReleaseNote(kind: "new", title: "Every Alta question", body: "Any question type now shows in the popup, on its own."),
            ReleaseNote(kind: "fixed", title: "Mastery moves", body: "Mastery and objectives update as you answer."),
            ReleaseNote(kind: "new", title: "Full-page screenshot", body: "Save Alta's whole page as one picture to send."),
        ]),
        Release(version: "1.3.20", date: "2026-10-10", notes: [
            ReleaseNote(kind: "new", title: "Maths answers in Alta", body: "Type maths answers in the popup, like -14/9."),
            ReleaseNote(kind: "fixed", title: "No Alta pop-ups", body: "Alta's welcome pop-ups are dismissed for you."),
        ]),
        Release(version: "1.3.19", date: "2026-10-10", notes: [
            ReleaseNote(kind: "fixed", title: "Alta start screen", body: "Alta's objectives and Start show in the popup again."),
        ]),
        Release(version: "1.3.18", date: "2026-10-10", notes: [
            ReleaseNote(kind: "improved", title: "Alta start screen", body: "Alta opens on its objectives, with a Start button."),
        ]),
        Release(version: "1.3.17", date: "2026-10-10", notes: [
            ReleaseNote(kind: "new", title: "Alta as a quiz", body: "Open Tool on Alta opens a quiz with objectives and mastery."),
            ReleaseNote(kind: "new", title: "Beta updates", body: "Settings ▸ Updates can now get beta versions early."),
        ]),
        Release(version: "1.3.16", date: "2026-10-10", notes: [
            ReleaseNote(kind: "improved", title: "Counters open in place", body: "A counter's list opens over its own card, not a corner."),
        ]),
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
