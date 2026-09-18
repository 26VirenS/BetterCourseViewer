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
  };
  self.BCV_WHATS_NEW = [
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
