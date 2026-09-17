/* What changed in each release, in plain words: the data behind the What's New page
 * (content/app/whatsnew.js), newest first. Every release ships its entry here — the suite fails
 * when the newest entry is not the manifest's version. A note is a kind (new | improved | fixed),
 * a short title (a few plain words, no cleverness), ONE short sentence saying what the student can
 * now do, and an icon path. Nothing is fetched: the notes travel with the build. Nothing internal
 * belongs here — if it does not change what the student sees, leave it out. */
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
    { version: '2.14.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'Away refresh, with a warning', body: 'Come back to a tab you left for a while and a small pill counts down three seconds before the page refreshes; click it to cancel.', icon: P.clock },
      { kind: 'improved', title: 'Persistent, without the small print', body: 'The line of explanation under the Persistent switch is gone.', icon: P.toggle },
    ] },
    { version: '2.13.3', date: '2026-09-17', notes: [
      { kind: 'improved', title: 'A simpler What’s new', body: 'One list of what changed and one button, nothing else.', icon: P.sparkle },
      { kind: 'fixed', title: 'Skipped updates show once', body: 'If you missed a few updates, everything since your last version is on one screen, shown once.', icon: P.layers },
    ] },
    { version: '2.13.2', date: '2026-09-17', notes: [
      { kind: 'improved', title: 'Smaller switch at the top right', body: 'The switch is now just the logo and the toggle; hover over it to see the name and the Persistent option.', icon: P.toggle },
    ] },
    { version: '2.13.1', date: '2026-09-17', notes: [
      { kind: 'fixed', title: 'Blue dot in the right place', body: 'On Windows and Linux the blue dot after “Simpl” now sits where it should.', icon: P.sparkle },
    ] },
    { version: '2.13.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'Persistent option on the switch', body: 'Hover over the switch at the top right to turn Persistent on or off.', icon: P.toggle },
      { kind: 'fixed', title: 'Mark as done shows up more often', body: 'The Mark as done button now shows for discussions and quizzes in modules too.', icon: P.check },
    ] },
    { version: '2.12.2', date: '2026-09-17', notes: [
      { kind: 'fixed', title: 'Grades are always up to date', body: 'Grades are loaded fresh from Canvas every time, so an old grade can no longer stick around.', icon: P.check },
    ] },
    { version: '2.12.1', date: '2026-09-17', notes: [
      { kind: 'fixed', title: 'Grades from tools show up', body: 'A grade posted by a tool now shows the next time you open Grades, no reload needed.', icon: P.check },
    ] },
    { version: '2.12.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'What’s new screen', body: 'After an update, the first Canvas page you open shows what changed.', icon: P.sparkle },
      { kind: 'new', title: 'Earlier versions', body: 'Press Earlier versions at the bottom of the list to read about older updates.', icon: P.layers },
    ] },
    { version: '2.11.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'Switch on every page', body: 'A small switch at the top right turns the Simpl Courses look on or off.', icon: P.toggle },
      { kind: 'new', title: 'Persistent option in the popup', body: 'Turn on Persistent to keep the look off (or on) across every page.', icon: P.check },
      { kind: 'improved', title: 'Open in stock Canvas', body: 'This button now changes only the page you are on, unless Persistent is on.', icon: P.eye },
    ] },
    { version: '2.10.1', date: '2026-09-17', notes: [
      { kind: 'improved', title: 'Classes are ticked for you', body: 'Courses with names like MATH 021 are selected for you during setup.', icon: P.check },
      { kind: 'improved', title: 'Scrollbar on the course list', body: 'The course list in setup always shows a scrollbar, so you can tell there are more courses below.', icon: P.scroll },
    ] },
    { version: '2.10.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'New guided setup', body: 'Setup now walks you through each step, with previews and a summary at the end.', icon: P.steps },
      { kind: 'improved', title: 'Dashboard previews', body: 'Pick Cards, List or Activity from small previews drawn in your own course colours.', icon: P.pic },
    ] },
    { version: '2.9.1', date: '2026-09-17', notes: [
      { kind: 'improved', title: 'Install page shows each step', body: 'The page after install now shows a picture for each step.', icon: P.pic },
    ] },
    { version: '2.7.5', date: '2026-09-16', notes: [
      { kind: 'fixed', title: 'Assignments open faster', body: 'Assignment pages no longer wait for the Mark as done and Previous / Next buttons before showing.', icon: P.clock },
    ] },
    { version: '2.7.3', date: '2026-09-16', notes: [
      { kind: 'new', title: 'Hide completed on the Dashboard', body: 'A Hide completed switch on the Dashboard list hides what you have already finished.', icon: P.list },
    ] },
    { version: '2.7.2', date: '2026-09-16', notes: [
      { kind: 'new', title: 'Feedback and grade for an assignment', body: 'Press the grade on an assignment to see the score, comments and every attempt.', icon: P.pen },
      { kind: 'new', title: 'Mark as done', body: 'Assignments that need to be marked done now have the button right on the page.', icon: P.check },
      { kind: 'new', title: 'Previous and Next', body: 'Move to the previous or next assignment from the page you are on.', icon: P.arrows },
    ] },
    { version: '2.7.1', date: '2026-09-16', notes: [
      { kind: 'fixed', title: 'Attachments open on the phone', body: 'Attachments on submissions now open properly instead of showing a 404.', icon: P.clip },
    ] },
  ];
})();
