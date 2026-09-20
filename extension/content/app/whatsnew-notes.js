/* What changed in each release: the data behind the What's New page (content/app/whatsnew.js),
 * newest first. Every release ships its entry here — the suite fails when the newest entry is not
 * the manifest's version. A note is a kind (new | improved | fixed), a title of two to four plain
 * words, ONE short plain line (under 90 characters — the suite holds both limits), and an icon
 * path. Simple words, no cleverness, nothing internal: if it does not change what the student
 * sees, leave it out. Nothing is fetched: the notes travel with the build. */
(function () {
  const P = {
    sparkle: 'M12 3l1.9 4.1L18 9l-4.1 1.9L12 15l-1.9-4.1L6 9l4.1-1.9z',
    layers: 'M12 4l8 4-8 4-8-4zM4 12l8 4 8-4M4 16l8 4 8-4',
    toggle: 'M3 12a5 5 0 015-5h8a5 5 0 010 10H8a5 5 0 01-5-5zM16 9.5v5',
    check: 'M20 6L9 17l-5-5',
    list: 'M5 6h14M5 12h14M5 18h9',
    scroll: 'M6 4h9v16H6zM18 7v10',
    eye: 'M2 12s4-7 10-7 10 7 10 7-4 7-10 7-10-7-10-7zM12 15a3 3 0 100-6 3 3 0 000 6z',
    steps: 'M4 19h16M7 16V9M12 16V5M17 16v-4',
    pic: 'M4 5h16v14H4zM4 15l4-4 4 4 3-3 5 5',
    pen: 'M4 20h4l10-10-4-4L4 16zM13 7l4 4',
    arrows: 'M4 12h16M10 6l-6 6 6 6M14 6l6 6-6 6',
    clip: 'M21 11l-9 9a5 5 0 01-7-7l9-9a3 3 0 014 4l-9 9a1 1 0 01-2-2l8-8',
    clock: 'M12 5a8 8 0 100 16 8 8 0 000-16zM12 9v4l3 2',
    tool: 'M14.5 3.5a5 5 0 0 0-6 6l-5 5V20h4l5-5a5 5 0 0 0 6-6l-3 3-3-3z',
    pin: 'M9 4h6l-1 5 3 3v2H7v-2l3-3zM12 14v6',
  };
  self.BCV_WHATS_NEW = [
    { version: '2.41.1', date: '2026-09-20', notes: [
      { kind: 'fixed', title: 'A stray “null”', body: 'The PDF annotator’s home no longer shows “nullnull” under its drop zone.', icon: P.check },
    ] },
    { version: '2.41.0', date: '2026-09-20', notes: [
      { kind: 'new', title: 'Periodic table', body: 'Every element in place with its facts; search by symbol, name or number; a pin too.', icon: P.layers },
    ] },
    { version: '2.40.0', date: '2026-09-20', notes: [
      { kind: 'improved', title: 'Tools fill the screen', body: 'A tool’s popup fills the screen but for its bar; the pins and the switch slide into it.', icon: P.layers },
      { kind: 'improved', title: 'Start assignment', body: 'An assignment done in a tool says Start or Continue assignment and opens it full screen.', icon: P.check },
      { kind: 'improved', title: 'Fewer Open in Canvas', body: 'Gone from tools and files; kept where it matters, like quizzes.', icon: P.toggle },
    ] },
    { version: '2.39.1', date: '2026-09-20', notes: [
      { kind: 'improved', title: 'A wider calculator', body: 'The Calculator tool takes more of the page, with bigger keys; its pin says Calculator.', icon: P.steps },
      { kind: 'improved', title: 'Desmos pin', body: 'Full screen at the top left opens the big graphing calculator; desmos.com at the right.', icon: P.sparkle },
    ] },
    { version: '2.39.0', date: '2026-09-20', notes: [
      { kind: 'new', title: 'Tools open over the page', body: 'Campus tools, module links and tools open in a popup that fills the tab; pins stay.', icon: P.layers },
      { kind: 'new', title: 'Calculator on its own', body: 'The scientific calculator is a tool of its own now, with the keyboard on it.', icon: P.steps },
      { kind: 'improved', title: 'Graphing pin', body: 'The graphing calculator’s pin swells into a small Desmos, portrait, that keeps its graph.', icon: P.sparkle },
    ] },
    { version: '2.38.0', date: '2026-09-20', notes: [
      { kind: 'new', title: 'The Cite pin opens a panel', body: 'Paste a link, cite the page you are on, pick the style, copy a saved citation.', icon: P.pin },
    ] },
    { version: '2.37.1', date: '2026-09-20', notes: [
      { kind: 'fixed', title: 'Citation generator laid out', body: 'Style and source sit on one row across the popup; the style switch has its room back.', icon: P.list },
    ] },
    { version: '2.37.0', date: '2026-09-20', notes: [
      { kind: 'improved', title: 'Image to text reads better', body: 'PaddleOCR now: sharper reading of pictures and scans, Chinese and Japanese too.', icon: P.eye },
      { kind: 'improved', title: 'Merge & split redrawn', body: 'Every page a thumbnail: drag pages into the merged document; take pages out to split.', icon: P.layers },
    ] },
    { version: '2.36.0', date: '2026-09-19', notes: [
      { kind: 'new', title: 'Draw and type on PDFs', body: 'A pen, text boxes, underline and strike-through, six colours, undo and redo.', icon: P.pen },
      { kind: 'improved', title: 'Citation generator', body: 'Source types as tiles, the fields still needed as chips, Today for the date, tidier.', icon: P.list },
      { kind: 'improved', title: 'The Cite pin', body: 'The citation pin is a plain button now: press it and the generator opens.', icon: P.pin },
      { kind: 'fixed', title: 'Away Refresh waits', body: 'No reload while a tool, a preview, a sheet or a hand-in is open; the page counts as awake.', icon: P.clock },
      { kind: 'fixed', title: 'New Quizzes are quizzes', body: 'A quiz taken in a frame Canvas launches is never reloaded out from under you either.', icon: P.check },
      { kind: 'improved', title: 'No pins in a quiz', body: 'The pinned tools are put away while a quiz attempt is going.', icon: P.pin },
    ] },
    { version: '2.35.1', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'The switch, shown again', body: 'The black screen for the switch comes back once, redrawn: each stop on a small slider.', icon: P.toggle },
      { kind: 'improved', title: 'Merge & split icon', body: 'The card wears a page with a cut line across it.', icon: P.layers },
    ] },
    { version: '2.35.0', date: '2026-09-19', notes: [
      { kind: 'new', title: 'Grade needed', body: 'What you need on the final to hit your goal, every number from Canvas.', icon: P.steps },
      { kind: 'new', title: 'Merge & split PDFs', body: 'Join PDFs into one, or cut one into parts, on this device.', icon: P.layers },
      { kind: 'new', title: 'PDF annotator', body: 'Highlight, box and pin notes on a PDF; kept per file, saved into a copy.', icon: P.pen },
      { kind: 'new', title: 'Image to text', body: 'Read the words off a picture or a scan, right here. Nothing is uploaded.', icon: P.pic },
      { kind: 'improved', title: 'A real calculator', body: 'The calculator pin now opens a scientific calculator, laid out like the app.', icon: P.tool },
      { kind: 'fixed', title: 'Pin fields on Canvas', body: 'The Cite pin’s field no longer comes out white and square on a real Canvas page.', icon: P.pin },
      { kind: 'improved', title: 'The lock in red-orange', body: 'The switch’s locked stop is red-orange now; the welcome shows each stop on a small slider.', icon: P.toggle },
    ] },
    { version: '2.34.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'Just the slider', body: 'The switch opens into the slider alone; the welcome now says what each part does.', icon: P.toggle },
    ] },
    { version: '2.33.0', date: '2026-09-19', notes: [
      { kind: 'new', title: 'How the switch works', body: 'A short show of the new switch, once: point, press each side, drag the knob.', icon: P.toggle },
      { kind: 'new', title: 'Quick menus on pins', body: 'Point at a pinned tool: cite a link, work out a sum, convert a file, study a set.', icon: P.pin },
      { kind: 'improved', title: 'Learn remembers', body: 'Wrong cards come back soon, learned ones return for a check in a day, then less often.', icon: P.layers },
      { kind: 'new', title: 'Share a set', body: 'Share sends the set as a CSV. A friend adds it with Import CSV.', icon: P.arrows },
    ] },
    { version: '2.32.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'Look switch folds to its mark', body: 'A small disc with the mark, in the state’s colour. Point at it and it opens up.', icon: P.toggle },
    ] },
    { version: '2.31.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'New look switch', body: 'A white knob with a tick, a dash or a lock in it, and the colour out from the middle.', icon: P.toggle },
    ] },
    { version: '2.30.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'Bigger look switch', body: 'The slider grows under the pointer. Press its left side to lock, its right side for on.', icon: P.toggle },
    ] },
    { version: '2.29.0', date: '2026-09-19', notes: [
      { kind: 'new', title: 'Three-way look switch', body: 'Press for stock Canvas on this page. Drag it left, or press twice, to lock Simpl off.', icon: P.toggle },
    ] },
    { version: '2.28.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'Word into a PDF hand-in', body: 'Where only PDF is allowed, the picker takes Word, text and pictures, and says so.', icon: P.arrows },
    ] },
    { version: '2.27.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'Timer opens on hover', body: 'Point at the pinned timer and it opens, like the switch beside it. Move away and it folds.', icon: P.pin },
    ] },
    { version: '2.26.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'Timer scale', body: 'The marker stays in the middle. Drag the scale left or right to set the minutes.', icon: P.clock },
      { kind: 'new', title: 'Set and go', body: 'The pinned timer opens a small scale and a Start button. Set it, and go.', icon: P.pin },
    ] },
    { version: '2.25.0', date: '2026-09-19', notes: [
      { kind: 'new', title: 'Convert to hand in', body: 'A file of the wrong type is offered as one the assignment takes, converted here.', icon: P.arrows },
      { kind: 'fixed', title: 'One Away Refresh', body: 'Coming back to a tab shows one pill, never a stack of them.', icon: P.clock },
      { kind: 'fixed', title: 'Tool grades', body: 'A grade a tool passes back shows on the assignment by itself, no reload needed.', icon: P.check },
      { kind: 'fixed', title: 'Safari: Open works', body: 'After the toolbar button, a preview’s Open goes through without a reload.', icon: P.check },
    ] },
    { version: '2.24.0', date: '2026-09-19', notes: [
      { kind: 'new', title: 'Flashcards, Quizlet style', body: 'A set page with Flashcards, Learn, Test and Match, stars, and paste to add cards.', icon: P.layers },
      { kind: 'improved', title: 'Simpler Tools', body: 'Every tool says what it does in a few plain words.', icon: P.tool },
    ] },
    { version: '2.23.0', date: '2026-09-18', notes: [
      { kind: 'new', title: 'Word ⇄ PDF here', body: 'Word to PDF and back on this device: headings, lists, tables, links and pictures.', icon: P.arrows },
      { kind: 'new', title: 'Pass/Fail courses', body: 'A switch on each course in setup. No letter to aim at, and it stays out of the GPA.', icon: P.toggle },
      { kind: 'improved', title: 'Install arrow', body: 'The arrow after install points at the puzzle piece itself.', icon: P.steps },
    ] },
    { version: '2.22.1', date: '2026-09-18', notes: [
      { kind: 'fixed', title: 'Access codes', body: 'The code goes with every answer, and is asked for again if Canvas wants it.', icon: P.check },
    ] },
    { version: '2.22.0', date: '2026-09-18', notes: [
      { kind: 'new', title: 'Quizzes with a code', body: 'A quiz that needs an access code asks for it first. Network limits are explained.', icon: P.check },
      { kind: 'new', title: 'Surveys', body: 'Surveys and graded surveys are taken here, in their own words.', icon: P.list },
      { kind: 'improved', title: 'Fresh on return', body: 'Come back to a tab and it reads Canvas again. Lists expire sooner.', icon: P.arrows },
      { kind: 'improved', title: 'Written answers', body: 'Essay answers keep their paragraphs and lists, in feedback too.', icon: P.pen },
      { kind: 'improved', title: 'Graded means done', body: 'Work that already has a grade is never shown as due.', icon: P.check },
    ] },
    { version: '2.21.0', date: '2026-09-18', notes: [
      { kind: 'new', title: 'Real conversions', body: 'Paste a CloudConvert key: Word, PDF, slides and sheets convert properly.', icon: P.arrows },
    ] },
    { version: '2.20.0', date: '2026-09-18', notes: [
      { kind: 'improved', title: 'After install', body: 'A splash, an arrow to the button, then where to go. Nothing to read.', icon: P.steps },
      { kind: 'improved', title: 'One button', body: 'The timer’s pin is its live activity. It morphs; no second button.', icon: P.toggle },
    ] },
    { version: '2.19.0', date: '2026-09-18', notes: [
      { kind: 'improved', title: 'Focus timer', body: 'Set the minutes on a scale, like the phone’s timer card.', icon: P.clock },
      { kind: 'improved', title: 'Live activity', body: 'A small dial beside the switch. Press it to see the count.', icon: P.toggle },
    ] },
    { version: '2.18.0', date: '2026-09-18', notes: [
      { kind: 'improved', title: 'Focus timer', body: 'A new dial, session dots, Skip, and smoother motion.', icon: P.clock },
      { kind: 'new', title: 'Live at the top right', body: 'A running timer sits next to the switch, not in the sidebar.', icon: P.toggle },
      { kind: 'improved', title: 'Tools in motion', body: 'Cards, popups and flashcards move as you use them.', icon: P.sparkle },
    ] },
    { version: '2.17.0', date: '2026-09-18', notes: [
      { kind: 'new', title: 'Tools', body: 'Citations, a focus timer, Desmos, a file converter and flashcards.', icon: P.tool },
      { kind: 'new', title: 'Pin a tool', body: 'Drag a tool card to the top. It becomes a button next to the switch.', icon: P.pin },
    ] },
    { version: '2.16.1', date: '2026-09-18', notes: [
      { kind: 'improved', title: 'Tips after setup', body: 'Continue waits a little longer.', icon: P.sparkle },
    ] },
    { version: '2.16.0', date: '2026-09-18', notes: [
      { kind: 'new', title: 'Tips after setup', body: 'Two quick tips replace the tour after setup.', icon: P.sparkle },
    ] },
    { version: '2.15.3', date: '2026-09-18', notes: [
      { kind: 'improved', title: 'Setup', body: 'You pick your courses yourself. The page reloads when you finish.', icon: P.steps },
    ] },
    { version: '2.15.2', date: '2026-09-18', notes: [
      { kind: 'fixed', title: 'Mark as done on pages', body: 'Pages in a module that ask for a mark have the button too.', icon: P.check },
    ] },
    { version: '2.15.1', date: '2026-09-18', notes: [
      { kind: 'new', title: 'Preview before you submit', body: 'Attached files have a Preview button.', icon: P.eye },
    ] },
    { version: '2.15.0', date: '2026-09-18', notes: [
      { kind: 'new', title: 'Clear overdue items', body: 'Press the X next to an overdue item to clear it.', icon: P.check },
      { kind: 'improved', title: 'Shorter install page', body: 'Three quick steps, a line each.', icon: P.pic },
    ] },
    { version: '2.14.3', date: '2026-09-18', notes: [
      { kind: 'improved', title: 'Timer hand', body: 'The hand now turns from the center.', icon: P.clock },
    ] },
    { version: '2.14.2', date: '2026-09-18', notes: [
      { kind: 'improved', title: 'Rounder timer hand', body: 'Thicker at the base, rounded at both ends.', icon: P.clock },
    ] },
    { version: '2.14.1', date: '2026-09-17', notes: [
      { kind: 'improved', title: 'New timer look', body: 'The countdown now looks like the iPhone timer.', icon: P.clock },
    ] },
    { version: '2.14.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'Away Refresh', body: 'Back after a while? The page refreshes after a 3 second countdown. Click to cancel.', icon: P.clock },
      { kind: 'improved', title: 'Cleaner Persistent switch', body: 'The extra text under it is gone.', icon: P.toggle },
    ] },
    { version: '2.13.3', date: '2026-09-17', notes: [
      { kind: 'improved', title: 'Simpler What’s new', body: 'Just a list of changes and one button.', icon: P.sparkle },
      { kind: 'fixed', title: 'Shows once', body: 'Skipped a few updates? You see one screen, once.', icon: P.layers },
    ] },
    { version: '2.13.2', date: '2026-09-17', notes: [
      { kind: 'improved', title: 'Smaller top-right switch', body: 'Hover to see the name and Persistent.', icon: P.toggle },
    ] },
    { version: '2.13.1', date: '2026-09-17', notes: [
      { kind: 'fixed', title: 'Blue dot fixed', body: 'The dot after “Simpl” is in the right spot on Windows.', icon: P.sparkle },
    ] },
    { version: '2.13.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'Persistent on the switch', body: 'Hover the top-right switch to turn Persistent on or off.', icon: P.toggle },
      { kind: 'fixed', title: 'Mark as done', body: 'Now shows for discussions and quizzes in modules.', icon: P.check },
    ] },
    { version: '2.12.2', date: '2026-09-17', notes: [
      { kind: 'fixed', title: 'Fresh grades', body: 'Grades always load fresh from Canvas.', icon: P.check },
    ] },
    { version: '2.12.1', date: '2026-09-17', notes: [
      { kind: 'fixed', title: 'Grades from tools', body: 'Grades posted by tools show up without a reload.', icon: P.check },
    ] },
    { version: '2.12.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'What’s new screen', body: 'See what changed after each update.', icon: P.sparkle },
      { kind: 'new', title: 'Earlier versions', body: 'Older updates are at the bottom of the list.', icon: P.layers },
    ] },
    { version: '2.11.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'Top-right switch', body: 'Turn the Simpl Courses look on or off on any page.', icon: P.toggle },
      { kind: 'new', title: 'Persistent', body: 'Keep the look on or off across every page.', icon: P.check },
      { kind: 'improved', title: 'Open in stock Canvas', body: 'Changes only this page unless Persistent is on.', icon: P.eye },
    ] },
    { version: '2.10.1', date: '2026-09-17', notes: [
      { kind: 'improved', title: 'Classes pre-selected', body: 'Setup ticks courses named like MATH 021.', icon: P.check },
      { kind: 'improved', title: 'Course list scrollbar', body: 'The setup course list always shows a scrollbar.', icon: P.scroll },
    ] },
    { version: '2.10.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'New setup', body: 'Step by step, with previews and a summary.', icon: P.steps },
      { kind: 'improved', title: 'Dashboard previews', body: 'Pick Cards, List or Activity from small previews.', icon: P.pic },
    ] },
    { version: '2.9.1', date: '2026-09-17', notes: [
      { kind: 'improved', title: 'Install page', body: 'Each step now has a picture.', icon: P.pic },
    ] },
    { version: '2.7.5', date: '2026-09-16', notes: [
      { kind: 'fixed', title: 'Faster assignments', body: 'Assignment pages open without waiting for buttons.', icon: P.clock },
    ] },
    { version: '2.7.3', date: '2026-09-16', notes: [
      { kind: 'new', title: 'Hide completed', body: 'Hide finished work on the Dashboard list.', icon: P.list },
    ] },
    { version: '2.7.2', date: '2026-09-16', notes: [
      { kind: 'new', title: 'Assignment feedback', body: 'Press the grade to see score, comments and attempts.', icon: P.pen },
      { kind: 'new', title: 'Mark as done', body: 'The button is right on the assignment page.', icon: P.check },
      { kind: 'new', title: 'Previous and Next', body: 'Jump between assignments from the page.', icon: P.arrows },
    ] },
    { version: '2.7.1', date: '2026-09-16', notes: [
      { kind: 'fixed', title: 'Phone attachments', body: 'Attachments now open instead of a 404.', icon: P.clip },
    ] },
  ];
})();
