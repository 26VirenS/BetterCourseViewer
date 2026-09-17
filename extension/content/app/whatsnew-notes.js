/* What changed in each release, in the student's terms: the data behind the What's New page
 * (content/app/whatsnew.js), newest first. Every release ships its entry here — the suite fails
 * when the newest entry is not the manifest's version. A note is a kind (new | improved | fixed),
 * a title of a few words, ONE sentence of what the student can now do, where to find it (a
 * breadcrumb, or blank), and an icon path. Nothing is fetched: the notes travel with the build.
 * Nothing internal belongs here — if it does not change what the student sees, leave it out. */
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
    { version: '2.13.1', date: '2026-09-17', notes: [
      { kind: 'fixed', title: 'The dot after Simpl, in its place', body: 'On Windows and Linux the word is drawn wider than on a Mac, and the blue dot landed on the l; it now sits where the word ends.', where: 'The setup and What’s new word-mark', icon: P.sparkle },
    ] },
    { version: '2.13.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'Persistent, under the look switch', body: 'Rest on the switch at the top right and a second row opens: Persistent, to choose whether the switch saves or changes this page only.', where: 'Top right of any Canvas page', icon: P.toggle },
      { kind: 'fixed', title: 'Mark as done, wherever a module asks', body: 'The button now shows for assignments that sit in a module as a discussion or a quiz, and whenever Canvas leaves the requirement out of its answer.', where: 'The assignment page', icon: P.check },
    ] },
    { version: '2.12.2', date: '2026-09-17', notes: [
      { kind: 'fixed', title: 'Never a days-old grade', body: 'Canvas is always asked afresh: nothing is read from the browser’s cache, so a grade posted days ago cannot go on missing.', where: 'Grades, and a course’s Grades tab', icon: P.check },
    ] },
    { version: '2.12.1', date: '2026-09-17', notes: [
      { kind: 'fixed', title: 'Grades from tools show up', body: 'A grade a tool posts while the page is open now shows on the next look at Grades, no reload needed.', where: 'Grades, and a course’s Grades tab', icon: P.check },
    ] },
    { version: '2.12.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'What’s new, after an update', body: 'The first Canvas page after an update shows what changed in that version, once, then gets out of the way.', where: 'Account → What’s new', icon: P.sparkle },
      { kind: 'new', title: 'Earlier versions, one link away', body: 'See earlier versions lists the releases before this one, newest first, with the ones you skipped marked.', where: 'What’s new → See earlier versions', icon: P.layers },
    ] },
    { version: '2.11.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'A look switch on every page', body: 'A small switch at the top right turns the Simpl Courses look off or on, over stock Canvas too.', where: 'Top right of any Canvas page', icon: P.toggle },
      { kind: 'new', title: 'Persistent, in the popup', body: 'Off, the look switch changes this page only; on, it saves and every page follows.', where: 'Toolbar popup → Persistent', icon: P.check },
      { kind: 'improved', title: 'Open in stock Canvas follows suit', body: 'On a page Canvas draws itself, the button is for this page only unless Persistent is on.', where: '', icon: P.eye },
    ] },
    { version: '2.10.1', date: '2026-09-17', notes: [
      { kind: 'improved', title: 'Classes come ticked in setup', body: 'Courses named like a class, MATH 021 or PHYS 008HL, are ticked for you; resource sites are not.', where: 'Guided setup → Your courses', icon: P.check },
      { kind: 'improved', title: 'A scrollbar on the course list', body: 'The setup’s course list shows its scrollbar at all times, so the courses below are not missed.', where: 'Guided setup → Your courses', icon: P.scroll },
    ] },
    { version: '2.10.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'A new guided setup', body: 'A rail of steps with your answers, previews of the dashboard and sidebar, and a read-back before Open Canvas.', where: 'Account → Guided setup', icon: P.steps },
      { kind: 'improved', title: 'Dashboard picked from previews', body: 'Cards, List and Activity are shown as miniatures drawn in your own course colours.', where: 'Guided setup → Dashboard', icon: P.pic },
    ] },
    { version: '2.9.1', date: '2026-09-17', notes: [
      { kind: 'improved', title: 'The page after install draws each step', body: 'Open your Canvas, press the toolbar button, press Set up: each one pictured, not only described.', where: '', icon: P.pic },
    ] },
    { version: '2.7.5', date: '2026-09-16', notes: [
      { kind: 'fixed', title: 'Assignments open at once', body: 'The assignment page no longer waits for Mark as done and Previous / Next before it shows.', where: '', icon: P.clock },
    ] },
    { version: '2.7.3', date: '2026-09-16', notes: [
      { kind: 'new', title: 'Hide what is done on the Dashboard', body: 'A small Hide completed switch on the Dashboard list keeps only what is still to do.', where: 'Dashboard → List', icon: P.list },
    ] },
    { version: '2.7.2', date: '2026-09-16', notes: [
      { kind: 'new', title: 'Feedback and grade for an assignment', body: 'The mark on an assignment opens a feedback screen with the score, comments and every attempt.', where: 'Any assignment → the grade', icon: P.pen },
      { kind: 'new', title: 'Mark as done', body: 'An assignment that asks to be marked done has the button on the page itself.', where: 'The assignment page', icon: P.check },
      { kind: 'new', title: 'Previous and Next', body: 'Move to the previous or the next assignment from the page you are on.', where: 'The assignment page', icon: P.arrows },
    ] },
    { version: '2.7.1', date: '2026-09-16', notes: [
      { kind: 'fixed', title: 'Attachments open on the phone', body: 'A submission’s attachment opens instead of a 404, and the phone follows the handoff.', where: '', icon: P.clip },
    ] },
  ];
})();
