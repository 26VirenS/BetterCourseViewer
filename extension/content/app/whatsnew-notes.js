/* What changed in each release: the data behind the What's New page (content/app/whatsnew.js),
 * newest first. Every release ships its entry here — the suite fails when the newest entry is not
 * the manifest's version. A note is a kind (new | improved | fixed), a title of two to four plain
 * words (22 characters at most), ONE short plain line (60 at most — the suite holds both limits),
 * and an icon path. Write it short: the fewest plain words that say what changed, no clause you
 * can cut, no cleverness, nothing internal. If it does not change what the student sees, leave it
 * out. Nothing is fetched: the notes travel with the build. */
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
    { version: '2.92.0', date: '2026-09-24', notes: [
      { kind: 'new', title: 'Widgets of your own', body: 'Tools → Add a widget: one HTML file becomes a tool.', icon: P.layers },
      { kind: 'new', title: 'Four starters', body: 'A countdown, a notes pad, a unit converter, a word counter.', icon: P.list },
      { kind: 'new', title: 'Safe by design', body: 'A widget runs in a sandbox: no page, no session, no network.', icon: P.check },
    ] },
    { version: '2.91.0', date: '2026-09-24', notes: [
      { kind: 'new', title: 'Search does things', body: 'Type / for commands: submit, download, convert, open…', icon: P.sparkle },
      { kind: 'new', title: 'Hand in from the box', body: '/submit opens the assignment’s hand-in block over the page.', icon: P.pen },
      { kind: 'improved', title: 'Instant results', body: 'From the first letter, the top hit chosen as you type.', icon: P.list },
      { kind: 'new', title: '/ from any screen', body: 'Press / or ⌘K anywhere: the Dashboard opens on the box.', icon: P.arrows },
      { kind: 'new', title: 'Sums in the box', body: '2*(3+4) or 15% of 80: the answer appears, Enter copies it.', icon: P.tool },
    ] },
    { version: '2.90.0', date: '2026-09-24', notes: [
      { kind: 'improved', title: 'Lighter pages', body: 'A page parses far less code; the rest loads when used.', icon: P.sparkle },
      { kind: 'improved', title: 'Photos, as needed', body: 'A tab reads only the pictures its own screen shows.', icon: P.pic },
      { kind: 'improved', title: 'Less busy work', body: 'Fewer writes and watchers; warm-ups one at a time.', icon: P.clock },
    ] },
    { version: '2.89.0', date: '2026-09-24', notes: [
      { kind: 'fixed', title: 'Dropped scores count', body: 'A drop-lowest rule is applied to the total and the rings.', icon: P.steps },
      { kind: 'fixed', title: 'All-day events', body: 'An all-day event sits on its own day, not the day before.', icon: P.clock },
      { kind: 'fixed', title: 'Inbox names', body: 'A conversation is named after the other people in it.', icon: P.list },
      { kind: 'fixed', title: 'Alerts in order', body: 'Overdue first, then due soon, graded, feedback, news.', icon: P.check },
      { kind: 'fixed', title: 'Reply to an entry', body: 'Reply-to can be cancelled; the reply lands where you chose.', icon: P.pen },
      { kind: 'fixed', title: 'Sheets and moves', body: 'A sheet left open closes when the screen changes under it.', icon: P.layers },
      { kind: 'fixed', title: 'iPhone app start', body: 'The app’s background script loads on the phone again.', icon: P.tool },
      { kind: 'improved', title: 'A lighter build', body: 'Old screens and styles nobody could reach are gone.', icon: P.sparkle },
    ] },
    { version: '2.88.0', date: '2026-09-24', notes: [
      { kind: 'fixed', title: 'The search box', body: 'No stray white field inside the pill any more.', icon: P.check },
    ] },
    { version: '2.87.0', date: '2026-09-24', notes: [
      { kind: 'new', title: 'Past points, charted', body: 'Add a day (or a term) and a GPA in GPA settings.', icon: P.steps },
      { kind: 'improved', title: 'The trend, whole', body: 'Eight points spread across all of it, not the last week.', icon: P.clock },
    ] },
    { version: '2.86.0', date: '2026-09-24', notes: [
      { kind: 'fixed', title: 'CSV upload, on Safari', body: 'The file is read before the picker is cleared.', icon: P.check },
    ] },
    { version: '2.85.0', date: '2026-09-24', notes: [
      { kind: 'new', title: 'Grades, on the Mac', body: 'The app’s window has the Grades section now.', icon: P.list },
      { kind: 'new', title: 'Download CSV', body: 'Your record, and this term’s courses, as one file.', icon: P.steps },
    ] },
    { version: '2.84.0', date: '2026-09-24', notes: [
      { kind: 'new', title: 'Events open a sheet', body: 'When, place, details; un-reserve an appointment.', icon: P.eye },
      { kind: 'new', title: 'Upload, on the page', body: 'The Grades page’s settings take the transcript CSV too.', icon: P.steps },
    ] },
    { version: '2.83.0', date: '2026-09-24', notes: [
      { kind: 'new', title: 'Upload your transcript', body: 'A CSV of past courses in Settings → Grades sets your record.', icon: P.steps },
    ] },
    { version: '2.82.0', date: '2026-09-24', notes: [
      { kind: 'fixed', title: 'Your classes only', body: 'Sections you are not in no longer show as course numbers.', icon: P.check },
    ] },
    { version: '2.81.0', date: '2026-09-24', notes: [
      { kind: 'fixed', title: 'The bar over a tool', body: 'Comes back after a reload. Blocked? Press the Simpl icon.', icon: P.layers },
      { kind: 'new', title: 'Keep the bar here', body: 'The popup asks the browser once for the tool’s site.', icon: P.check },
      { kind: 'improved', title: 'Appearance, expanded', body: 'Light, Dark or Personalize, off the sidebar button.', icon: P.toggle },
      { kind: 'improved', title: 'Appointments, tidier', body: 'The class first; a day opens to its hours, not pills.', icon: P.list },
      { kind: 'fixed', title: 'Calendar head fits', body: 'Its buttons shorten on a narrower window, no wrapped row.', icon: P.arrows },
    ] },
    { version: '2.80.0', date: '2026-09-24', notes: [
      { kind: 'new', title: 'Find appointment', body: 'Office hours and sign-ups: a sheet of times to reserve.', icon: P.clock },
      { kind: 'improved', title: 'Month, uncluttered', body: 'Only the time you reserve goes on the calendar.', icon: P.eye },
    ] },
    { version: '2.79.0', date: '2026-09-24', notes: [
      { kind: 'fixed', title: 'Search box, tidied', body: 'One pill, level with the view switcher.', icon: P.check },
      { kind: 'fixed', title: 'Pages and files found', body: 'Every starred course is asked, a few at a time.', icon: P.pic },
      { kind: 'new', title: 'Wikipedia, a switch', body: 'The W beside the box turns it off, or on again.', icon: P.toggle },
      { kind: 'improved', title: 'Updates, every 4 hours', body: 'The Mac app checks four times a day, not hourly.', icon: P.clock },
    ] },
    { version: '2.78.0', date: '2026-09-24', notes: [
      { kind: 'new', title: 'Search everything', body: 'One box on the Dashboard: all of Canvas, and Wikipedia.', icon: P.sparkle },
      { kind: 'new', title: 'Shown once', body: 'A pointer at the box the first time, on black.', icon: P.eye },
    ] },
    // invite: the invitation again (whatsnew.js), for anyone who has not tried a theme yet; the notes wait in Settings
    { version: '2.77.0', date: '2026-09-23', invite: true, notes: [
      { kind: 'new', title: 'A cursor shows the way', body: 'It presses the sidebar and two counters, once.', icon: P.eye },
      { kind: 'improved', title: 'Try a theme, again', body: 'The invitation comes back for anyone who has not.', icon: P.pic },
    ] },
    { version: '2.76.0', date: '2026-09-23', notes: [
      { kind: 'improved', title: 'Personalize, laid out', body: 'A larger preview, the ready-made themes down its right.', icon: P.layers },
      { kind: 'improved', title: 'Continue at the foot', body: 'A bar at the bottom of the screen, on any device.', icon: P.arrows },
    ] },
    { version: '2.75.0', date: '2026-09-23', notes: [
      { kind: 'improved', title: 'Scenes redrawn', body: 'Ridges, waves, trees and dunes, in clean lines.', icon: P.pen },
      { kind: 'new', title: 'A drawing per card', body: 'Every counter and header wears its own variation.', icon: P.pic },
      { kind: 'fixed', title: 'Picker in place', body: 'The colour picker opens on the Custom swatch.', icon: P.tool },
      { kind: 'improved', title: 'Continue in view', body: 'The first screen fits shorter windows.', icon: P.arrows },
    ] },
    // invite: the first page after this update shows the theme invitation (whatsnew.js) in the notes' place; the notes wait in Settings
    { version: '2.74.0', date: '2026-09-23', invite: true, notes: [
      { kind: 'new', title: 'Try a theme', body: 'This update invites you to pick one, or a colour.', icon: P.pic },
      { kind: 'new', title: 'Themes, pointed out', body: 'After Personalize, the Appearance button is shown.', icon: P.eye },
    ] },
    { version: '2.73.0', date: '2026-09-23', notes: [
      { kind: 'improved', title: 'Appearance opens it', body: 'The sidebar’s Appearance button opens the editor.', icon: P.toggle },
      { kind: 'improved', title: 'Account panel, trimmed', body: 'Canvas’s own settings, and a link to Simpl’s.', icon: P.list },
      { kind: 'improved', title: 'What’s new in Settings', body: 'Settings → General opens it again.', icon: P.sparkle },
    ] },
    { version: '2.72.0', date: '2026-09-23', notes: [
      { kind: 'improved', title: 'Inked, tone on tone', body: 'Outlines a shade lighter than the ground they sit on.', icon: P.pen },
    ] },
    { version: '2.71.0', date: '2026-09-23', notes: [
      { kind: 'new', title: 'Photos, inked', body: 'Drawn in two inks: your colour and its complement.', icon: P.pen },
      { kind: 'improved', title: 'Scenes, inked too', body: 'Dusk, Ocean, Forest and Sand come as outlines and shapes.', icon: P.pic },
    ] },
    { version: '2.70.0', date: '2026-09-23', notes: [
      { kind: 'improved', title: 'Photos, a hint', body: 'Washed heavily in the look’s ground, its ink over them.', icon: P.pic },
      { kind: 'improved', title: 'Wide windows', body: 'The page fills a zoomed-out window instead of a band.', icon: P.arrows },
    ] },
    { version: '2.69.0', date: '2026-09-23', notes: [
      { kind: 'improved', title: 'Lighter on memory', body: 'Photos blur as pictures, not filters; Desmos lets go.', icon: P.tool },
      { kind: 'improved', title: 'Bolder colour', body: 'The greys take more of your colour.', icon: P.pen },
      { kind: 'improved', title: 'Photos washed', body: 'Darker on the dark look, lighter on the light.', icon: P.pic },
      { kind: 'improved', title: 'Sidebar reads', body: 'A soft halo behind the words, a hover you can see.', icon: P.eye },
      { kind: 'improved', title: 'Solid sheets', body: 'A counter’s sheet is solid, not glass.', icon: P.layers },
      { kind: 'improved', title: 'Clearer pins', body: 'The tray’s glyphs are bigger and brighter.', icon: P.sparkle },
      { kind: 'fixed', title: 'Selection ring', body: 'A photo choice shows its whole ring.', icon: P.check },
    ] },
    { version: '2.68.0', date: '2026-09-23', notes: [
      { kind: 'fixed', title: 'Sidebar photo stays', body: 'The photo holds still while the sidebar scrolls.', icon: P.pin },
      { kind: 'improved', title: 'Appearance', body: 'The sidebar’s switch just says Appearance.', icon: P.toggle },
      { kind: 'new', title: 'Default, ready-made', body: 'A tile that puts everything back: Regular, no photos.', icon: P.sparkle },
    ] },
    { version: '2.67.0', date: '2026-09-23', notes: [
      { kind: 'new', title: 'Ready-made looks', body: 'Dusk, Ocean, Forest, Sand: a colour and scenes at once.', icon: P.sparkle },
      { kind: 'improved', title: 'Drawn scenes, crisp', body: 'The four scenes are drawn as vectors: sharp at any size.', icon: P.pic },
      { kind: 'improved', title: 'A soft cast of colour', body: 'The greys take a little of your colour, so nothing jars.', icon: P.pen },
      { kind: 'improved', title: 'Frosted controls', body: 'Over a photo, buttons and rows are frosted glass.', icon: P.layers },
      { kind: 'fixed', title: 'Photo to the top', body: 'A header photo runs on up under the widgets’ bar.', icon: P.pin },
    ] },
    { version: '2.66.0', date: '2026-09-23', notes: [
      { kind: 'improved', title: 'Windows text', body: 'Segoe UI on Windows, so titles and numbers sit right.', icon: P.pen },
      { kind: 'improved', title: 'Bar over tools', body: 'On a school’s own Canvas too, asking once for a tool’s site.', icon: P.layers },
      { kind: 'fixed', title: 'Page titles', body: 'Calendar and Inbox titles are the size of the rest.', icon: P.check },
    ] },
    { version: '2.65.0', date: '2026-09-23', notes: [
      { kind: 'new', title: 'Personalize', body: 'A new look step: colour, course colours and page photos.', icon: P.sparkle },
      { kind: 'improved', title: 'A round colour picker', body: 'Pick a hue on the ring, then its saturation and depth.', icon: P.pen },
      { kind: 'improved', title: 'Shorter setup', body: 'Four steps, then Continue to appearance.', icon: P.steps },
    ] },
    { version: '2.64.0', date: '2026-09-23', notes: [
      { kind: 'improved', title: 'Blur in its own colour', body: 'A photo fades into a colour of its own, not black or white.', icon: P.pic },
      { kind: 'fixed', title: 'Depth slider', body: 'Dragging Depth no longer trips the Theme step in Safari.', icon: P.check },
    ] },
    { version: '2.63.0', date: '2026-09-23', notes: [
      { kind: 'fixed', title: 'Headers stay put', body: 'A header with a photo sits at the top again, and sticks.', icon: P.pin },
      { kind: 'fixed', title: 'The × on a photo', body: 'Taking a photo off a counter works in Safari too.', icon: P.check },
    ] },
    { version: '2.62.0', date: '2026-09-23', notes: [
      { kind: 'improved', title: 'A preview to scale', body: 'The theme’s Dashboard is the real page, made small.', icon: P.eye },
      { kind: 'improved', title: 'A tidier colour picker', body: 'The wheel and swatches take less room on the step.', icon: P.layers },
    ] },
    { version: '2.61.0', date: '2026-09-23', notes: [
      { kind: 'new', title: 'A colour wheel', body: 'Pick your colour on a wheel; sliders are one press away.', icon: P.pen },
      { kind: 'new', title: 'Colour your courses', body: 'A setup step gives each course its colour.', icon: P.sparkle },
      { kind: 'new', title: 'Photos on headers', body: 'Any page title can carry a photo, blurred towards it.', icon: P.pic },
      { kind: 'improved', title: 'A shade per row', body: 'Under a theme, each sidebar row wears its own shade.', icon: P.layers },
      { kind: 'improved', title: 'Titles in your colour', body: 'Page titles take the colour you chose.', icon: P.pen },
      { kind: 'improved', title: 'A roomier setup', body: 'Every step is larger, with a note to scroll for more.', icon: P.scroll },
    ] },
    { version: '2.60.0', date: '2026-09-23', notes: [
      { kind: 'new', title: 'Themes', body: 'Your own colour, and photos on the counters and sidebar.', icon: P.pic },
      { kind: 'new', title: 'Readable colours only', body: 'A picker that stops at colours you can read, day and night.', icon: P.eye },
      { kind: 'improved', title: 'See what you handed in', body: 'The chip by the title opens your work before it is graded.', icon: P.clip },
    ] },
    { version: '2.59.0', date: '2026-09-22', notes: [
      { kind: 'improved', title: 'Sidebar to the top', body: 'The widgets’ bar now sits beside it, over the page alone.', icon: P.layers },
      { kind: 'fixed', title: 'Quick flashcard marks', body: 'Fast presses each mark their own card, none twice.', icon: P.check },
    ] },
    { version: '2.58.0', date: '2026-09-22', notes: [
      { kind: 'new', title: 'A new Simpl switch', body: 'Hover the disc: Activate, Deactivate and Turn off drop down.', icon: P.toggle },
      { kind: 'new', title: 'Widgets get a bar', body: 'The switch and your pins sit in a strip above the page.', icon: P.pin },
      { kind: 'fixed', title: 'No flash between steps', body: 'The welcome stays black from one pointer to the next.', icon: P.eye },
      { kind: 'fixed', title: 'Arrows on their rows', body: 'A pointer at a sidebar row now sits level with it.', icon: P.arrows },
      { kind: 'improved', title: 'Course page, up top', body: 'The button to the course’s Grades page now sits by the X.', icon: P.arrows },
      { kind: 'fixed', title: 'What-if banner sized', body: 'The red note in Details no longer fills the column.', icon: P.check },
      { kind: 'fixed', title: 'One solid trend line', body: 'The line no longer breaks into dashes on the Mac.', icon: P.steps },
      { kind: 'fixed', title: 'Clean trend labels', body: 'No mark drawn through the numbers under the line.', icon: P.check },
    ] },
    { version: '2.57.0', date: '2026-09-22', notes: [
      { kind: 'new', title: 'What-if on Grades', body: 'Try scores in a course’s Details and see the total move.', icon: P.pen },
      { kind: 'improved', title: 'Details, side by side', body: 'Breakdown on the left, assignments on the right, one size.', icon: P.layers },
      { kind: 'improved', title: 'A wider calendar', body: 'The calendars moved behind a button; the month has the room.', icon: P.arrows },
      { kind: 'new', title: 'Preview from the grid', body: 'Press an assignment on the calendar to read it there.', icon: P.eye },
      { kind: 'new', title: 'Due tomorrow', body: 'A dashboard card for tomorrow’s work, in place of classes.', icon: P.clock },
      { kind: 'improved', title: 'Sheets hold still', body: 'A card’s list opens at one size and stays there.', icon: P.list },
      { kind: 'new', title: 'Pick a look in setup', body: 'Setup now asks light, dark or automatic.', icon: P.toggle },
      { kind: 'fixed', title: 'Widgets blur on Chrome', body: 'The glass behind a pinned tool frosts the page again.', icon: P.sparkle },
    ] },
    { version: '2.56.0', date: '2026-09-22', notes: [
      { kind: 'new', title: 'Your pins go with you', body: 'The pinned tools sit in the bar on a tool’s own tab.', icon: P.pin },
    ] },
    { version: '2.55.0', date: '2026-09-22', notes: [
      { kind: 'improved', title: 'Tools get a whole tab', body: 'A tool opens in its own tab, with our bar on top.', icon: P.layers },
      { kind: 'new', title: 'The X brings you back', body: 'It closes the tool and returns to your Canvas tab.', icon: P.arrows },
      { kind: 'fixed', title: 'Sign-ins just work', body: 'Nothing is taken from a tool while it signs you in.', icon: P.check },
      { kind: 'fixed', title: 'Matching saves again', body: 'A pick that Canvas used to refuse now goes through.', icon: P.list },
    ] },
    { version: '2.54.0', date: '2026-09-22', notes: [
      { kind: 'fixed', title: 'Blanks sit in the line', body: 'A dropdown fills the gap it was written into.', icon: P.pen },
      { kind: 'fixed', title: 'Every blank counts', body: 'A question is answered once no blank is left.', icon: P.check },
    ] },
    { version: '2.53.0', date: '2026-09-22', notes: [
      { kind: 'improved', title: 'Sign-ins finish first', body: 'A tool’s window runs to the end before it is brought back.', icon: P.clock },
      { kind: 'new', title: 'It says when to wait', body: 'The bar goes red while a tool is signing in somewhere else.', icon: P.eye },
    ] },
    { version: '2.52.0', date: '2026-09-22', notes: [
      { kind: 'improved', title: 'No tool is given up on', body: 'A slow one is waited for, not pushed into a tab.', icon: P.clock },
    ] },
    { version: '2.51.0', date: '2026-09-22', notes: [
      { kind: 'improved', title: 'No tab at all', body: 'A window a tool opens is taken before the browser makes one.', icon: P.layers },
    ] },
    { version: '2.50.1', date: '2026-09-22', notes: [
      { kind: 'fixed', title: 'Catching in Safari', body: 'Safari was never told a tab had opened. Now it is.', icon: P.layers },
    ] },
    { version: '2.50.0', date: '2026-09-22', notes: [
      { kind: 'fixed', title: 'Only Canvas is added', body: 'A tool’s own site is no longer taken for your Canvas.', icon: P.check },
      { kind: 'improved', title: 'Catching windows', body: 'More ways to catch one, for browsers that say less.', icon: P.layers },
    ] },
    { version: '2.49.0', date: '2026-09-21', notes: [
      { kind: 'new', title: 'Windows tools open', body: 'A window a tool opens for itself lands in a popup.', icon: P.layers },
      { kind: 'improved', title: 'Every quiz taken here', body: 'A quiz that seals its questions no longer opens on Canvas.', icon: P.check },
      { kind: 'fixed', title: 'Discussions in order', body: 'Ordered by the date each row shows, newest activity first.', icon: P.list },
      { kind: 'improved', title: 'Longer to open', body: 'A tool has ten seconds to open before it is given a tab.', icon: P.clock },
    ] },
    { version: '2.48.1', date: '2026-09-21', notes: [
      { kind: 'new', title: 'A tool over a tool', body: 'One opened from another goes on top, then hands back.', icon: P.layers },
      { kind: 'improved', title: 'Dark is for the framed', body: 'Only a popup holding another site goes dark.', icon: P.eye },
    ] },
    { version: '2.48.0', date: '2026-09-21', notes: [
      { kind: 'new', title: 'Framed tools go dark', body: 'A school tool opens dark, bar and page alike.', icon: P.eye },
      { kind: 'new', title: 'A sun for light', body: 'Press the sun in its bar to turn the pair light again.', icon: P.sparkle },
      { kind: 'improved', title: 'Desmos goes dark too', body: 'The graphing calculator is turned over to match.', icon: P.tool },
    ] },
    { version: '2.47.2', date: '2026-09-21', notes: [
      { kind: 'fixed', title: 'Pins show their icons', body: 'The panel’s blur was covering them.', icon: P.eye },
      { kind: 'new', title: 'A widget over a tool', body: 'The green light opens a tool over the one already up.', icon: P.layers },
      { kind: 'improved', title: 'The X keeps to itself', body: 'The red unpin dot shows only on its own corner.', icon: P.pin },
      { kind: 'fixed', title: 'Loose widgets stay put', body: 'One pulled out no longer folds when you hover another pin.', icon: P.pin },
      { kind: 'fixed', title: 'Pins press again', body: 'A press on a folded pin no longer starts pulling it out.', icon: P.pin },
    ] },
    { version: '2.47.1', date: '2026-09-21', notes: [
      { kind: 'improved', title: 'Widgets redesigned', body: 'Blurred, rounder, one box each: the pins are panels now.', icon: P.layers },
      { kind: 'new', title: 'Pull a widget out', body: 'Drag the bar under it and it stays where you put it.', icon: P.pin },
      { kind: 'improved', title: 'Easier to read', body: 'The welcome’s grey lines are larger and brighter.', icon: P.eye },
      { kind: 'improved', title: 'Stuck tools get a tab', body: 'One that has not opened in five seconds opens in a tab.', icon: P.clock },
    ] },
    { version: '2.47.0', date: '2026-09-21', notes: [
      { kind: 'improved', title: 'The welcome, again', body: 'See it again in Settings replays it; the old tour is gone.', icon: P.eye },
      { kind: 'improved', title: 'Updates land at once', body: 'After an update, your open Canvas tabs load again.', icon: P.arrows },
      { kind: 'fixed', title: 'The app’s switches act', body: 'In the Mac app, the two switches ask first, then do it.', icon: P.toggle },
    ] },
    { version: '2.46.0', date: '2026-09-21', notes: [
      { kind: 'improved', title: 'A fuller welcome', body: 'The black screen now points out Grades, Courses and Tools.', icon: P.eye },
    ] },
    { version: '2.45.1', date: '2026-09-21', notes: [
      { kind: 'improved', title: 'A cleaner table', body: 'Elements lose their outlines: the colour alone marks each.', icon: P.layers },
    ] },
    { version: '2.45.0', date: '2026-09-21', notes: [
      { kind: 'improved', title: 'Algebraic calculator', body: 'Type the whole sum and it is worked out as you go.', icon: P.tool },
    ] },
    { version: '2.44.2', date: '2026-09-20', notes: [
      { kind: 'new', title: 'Safari finds Canvas', body: 'Let it see every website once and your Canvas is found.', icon: P.sparkle },
      { kind: 'fixed', title: 'Updates find home', body: 'On a Mac, an update goes into Applications when it must.', icon: P.arrows },
    ] },
    { version: '2.44.1', date: '2026-09-20', notes: [
      { kind: 'new', title: 'A first screen', body: 'The Mac app opens on a welcome, and a button for Safari.', icon: P.sparkle },
      { kind: 'fixed', title: 'The app finds its home', body: 'Opened from Downloads, it offers to move to Applications.', icon: P.tool },
      { kind: 'improved', title: 'Smoother pins', body: 'A pinned tool opens under the pointer without a stutter.', icon: P.pin },
    ] },
    { version: '2.44.0', date: '2026-09-20', notes: [
      { kind: 'new', title: 'Settings in the app', body: 'On a Mac it is set up from its app; Safari follows.', icon: P.tool },
      { kind: 'new', title: 'The app updates itself', body: 'It checks every hour and installs a new version itself.', icon: P.arrows },
    ] },
    { version: '2.43.1', date: '2026-09-20', notes: [
      { kind: 'improved', title: 'A small periodic table', body: 'Hover its pin for the whole table, small; press to open.', icon: P.layers },
    ] },
    { version: '2.43.0', date: '2026-09-20', notes: [
      { kind: 'new', title: 'Setup finds Canvas', body: 'Install, open your Canvas, and setup begins there.', icon: P.sparkle },
      { kind: 'improved', title: 'A shorter first page', body: 'The page after install says Open your Canvas, then closes.', icon: P.steps },
    ] },
    { version: '2.42.0', date: '2026-09-20', notes: [
      { kind: 'improved', title: 'Groups open faster', body: 'The Groups list and a group’s page land sooner.', icon: P.clock },
      { kind: 'new', title: 'Reload a tool', body: 'A tool’s bar has Reload, for a sign-in gone in circles.', icon: P.arrows },
      { kind: 'improved', title: 'A quieter tool bar', body: 'A tool’s full-screen bar shows its name and nothing more.', icon: P.layers },
    ] },
    { version: '2.41.1', date: '2026-09-20', notes: [
      { kind: 'fixed', title: 'A stray “null”', body: 'The PDF annotator no longer shows “nullnull” on its home.', icon: P.check },
    ] },
    { version: '2.41.0', date: '2026-09-20', notes: [
      { kind: 'new', title: 'Periodic table', body: 'Every element with its facts, and a search across them.', icon: P.layers },
    ] },
    { version: '2.40.0', date: '2026-09-20', notes: [
      { kind: 'improved', title: 'Tools fill the screen', body: 'A tool’s popup fills the screen but for its bar.', icon: P.layers },
      { kind: 'improved', title: 'Start assignment', body: 'An assignment done in a tool opens it over the page.', icon: P.check },
      { kind: 'improved', title: 'Fewer Open in Canvas', body: 'Gone from tools and files; kept where it matters.', icon: P.toggle },
    ] },
    { version: '2.39.1', date: '2026-09-20', notes: [
      { kind: 'improved', title: 'A wider calculator', body: 'The Calculator takes more of the page, with bigger keys.', icon: P.steps },
      { kind: 'improved', title: 'Desmos pin', body: 'The pin opens the big graphing calculator, or desmos.com.', icon: P.sparkle },
    ] },
    { version: '2.39.0', date: '2026-09-20', notes: [
      { kind: 'new', title: 'Tools open over pages', body: 'Campus tools and module links open in a popup; pins stay.', icon: P.layers },
      { kind: 'new', title: 'Calculator on its own', body: 'The scientific calculator is a tool of its own now.', icon: P.steps },
      { kind: 'improved', title: 'Graphing pin', body: 'The pin swells into a small Desmos that keeps its graph.', icon: P.sparkle },
    ] },
    { version: '2.38.0', date: '2026-09-20', notes: [
      { kind: 'new', title: 'The Cite pin, a panel', body: 'Paste a link, cite this page, pick the style.', icon: P.pin },
    ] },
    { version: '2.37.1', date: '2026-09-20', notes: [
      { kind: 'fixed', title: 'Cite, laid out', body: 'Style and source sit on one row across the popup.', icon: P.list },
    ] },
    { version: '2.37.0', date: '2026-09-20', notes: [
      { kind: 'improved', title: 'Sharper image to text', body: 'PaddleOCR now: sharper on scans, with Chinese and Japanese.', icon: P.eye },
      { kind: 'improved', title: 'Merge & split redrawn', body: 'Every page a thumbnail: drag them in, or pull them out.', icon: P.layers },
    ] },
    { version: '2.36.0', date: '2026-09-19', notes: [
      { kind: 'new', title: 'Draw and type on PDFs', body: 'A pen, text boxes, underline, six colours, undo and redo.', icon: P.pen },
      { kind: 'improved', title: 'Citation generator', body: 'Source types as tiles, missing fields as chips, tidier.', icon: P.list },
      { kind: 'improved', title: 'The Cite pin', body: 'A plain button now: press it and the generator opens.', icon: P.pin },
      { kind: 'fixed', title: 'Away Refresh waits', body: 'No reload while a tool, a preview or a hand-in is open.', icon: P.clock },
      { kind: 'fixed', title: 'New Quizzes count', body: 'A quiz in a frame Canvas launches is never reloaded.', icon: P.check },
      { kind: 'improved', title: 'No pins in a quiz', body: 'The pinned tools are put away while a quiz attempt is going.', icon: P.pin },
    ] },
    { version: '2.35.1', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'The switch, shown', body: 'Its black screen comes back once, redrawn as a slider.', icon: P.toggle },
      { kind: 'improved', title: 'Merge & split icon', body: 'The card wears a page with a cut line across it.', icon: P.layers },
    ] },
    { version: '2.35.0', date: '2026-09-19', notes: [
      { kind: 'new', title: 'Grade needed', body: 'What you need on the final to hit your goal.', icon: P.steps },
      { kind: 'new', title: 'Merge & split PDFs', body: 'Join PDFs into one, or cut one into parts, on this device.', icon: P.layers },
      { kind: 'new', title: 'PDF annotator', body: 'Highlight, box and pin notes on a PDF, kept per file.', icon: P.pen },
      { kind: 'new', title: 'Image to text', body: 'Read the words off a picture. Nothing is uploaded.', icon: P.pic },
      { kind: 'improved', title: 'A real calculator', body: 'The calculator pin now opens a scientific calculator.', icon: P.tool },
      { kind: 'fixed', title: 'Pin fields on Canvas', body: 'The Cite pin’s field is no longer white and square.', icon: P.pin },
      { kind: 'improved', title: 'The lock in red-orange', body: 'The switch’s locked stop is red-orange now.', icon: P.toggle },
    ] },
    { version: '2.34.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'Just the slider', body: 'The switch opens into the slider alone.', icon: P.toggle },
    ] },
    { version: '2.33.0', date: '2026-09-19', notes: [
      { kind: 'new', title: 'How the switch works', body: 'A short show, once: point, press a side, drag the knob.', icon: P.toggle },
      { kind: 'new', title: 'Quick menus on pins', body: 'Point at a pinned tool: cite, work out, convert, study.', icon: P.pin },
      { kind: 'improved', title: 'Learn remembers', body: 'Wrong cards come back soon, learned ones in a day.', icon: P.layers },
      { kind: 'new', title: 'Share a set', body: 'Share sends the set as a CSV; Import CSV adds it.', icon: P.arrows },
    ] },
    { version: '2.32.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'The switch folds', body: 'A small disc with the mark. Point at it and it opens.', icon: P.toggle },
    ] },
    { version: '2.31.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'New look switch', body: 'A white knob with a tick, a dash or a lock in it.', icon: P.toggle },
    ] },
    { version: '2.30.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'Bigger look switch', body: 'The slider grows under the pointer. Press a side to set.', icon: P.toggle },
    ] },
    { version: '2.29.0', date: '2026-09-19', notes: [
      { kind: 'new', title: 'Three-way look switch', body: 'Press for stock Canvas; drag it left to lock Simpl off.', icon: P.toggle },
    ] },
    { version: '2.28.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'Word into a PDF', body: 'Where only PDF is allowed, the picker takes Word too.', icon: P.arrows },
    ] },
    { version: '2.27.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'Timer opens on hover', body: 'Point at the pinned timer and it opens, like the switch.', icon: P.pin },
    ] },
    { version: '2.26.0', date: '2026-09-19', notes: [
      { kind: 'improved', title: 'Timer scale', body: 'The marker stays put. Drag the scale to set the minutes.', icon: P.clock },
      { kind: 'new', title: 'Set and go', body: 'The pinned timer opens a small scale and a Start button.', icon: P.pin },
    ] },
    { version: '2.25.0', date: '2026-09-19', notes: [
      { kind: 'new', title: 'Convert to hand in', body: 'A file of the wrong type is converted here to one it takes.', icon: P.arrows },
      { kind: 'fixed', title: 'One Away Refresh', body: 'Coming back to a tab shows one pill, never a stack of them.', icon: P.clock },
      { kind: 'fixed', title: 'Tool grades', body: 'A grade a tool passes back shows without a reload.', icon: P.check },
      { kind: 'fixed', title: 'Safari: Open works', body: 'A preview’s Open goes through without a reload.', icon: P.check },
    ] },
    { version: '2.24.0', date: '2026-09-19', notes: [
      { kind: 'new', title: 'Flashcards', body: 'Flashcards, Learn, Test and Match, stars, paste to add.', icon: P.layers },
      { kind: 'improved', title: 'Simpler Tools', body: 'Every tool says what it does in a few plain words.', icon: P.tool },
    ] },
    { version: '2.23.0', date: '2026-09-18', notes: [
      { kind: 'new', title: 'Word ⇄ PDF here', body: 'Word to PDF and back here, formatting kept.', icon: P.arrows },
      { kind: 'new', title: 'Pass/Fail courses', body: 'A switch per course in setup; it stays out of the GPA.', icon: P.toggle },
      { kind: 'improved', title: 'Install arrow', body: 'The arrow after install points at the puzzle piece itself.', icon: P.steps },
    ] },
    { version: '2.22.1', date: '2026-09-18', notes: [
      { kind: 'fixed', title: 'Access codes', body: 'The code goes with every answer, and is asked again.', icon: P.check },
    ] },
    { version: '2.22.0', date: '2026-09-18', notes: [
      { kind: 'new', title: 'Quizzes with a code', body: 'A quiz that needs an access code asks for it first.', icon: P.check },
      { kind: 'new', title: 'Surveys', body: 'Surveys and graded surveys are taken here.', icon: P.list },
      { kind: 'improved', title: 'Fresh on return', body: 'Come back to a tab and it reads Canvas again.', icon: P.arrows },
      { kind: 'improved', title: 'Written answers', body: 'Essay answers keep their paragraphs and lists.', icon: P.pen },
      { kind: 'improved', title: 'Graded means done', body: 'Work that already has a grade is never shown as due.', icon: P.check },
    ] },
    { version: '2.21.0', date: '2026-09-18', notes: [
      { kind: 'new', title: 'Real conversions', body: 'Paste a CloudConvert key and Word, PDF and slides convert.', icon: P.arrows },
    ] },
    { version: '2.20.0', date: '2026-09-18', notes: [
      { kind: 'improved', title: 'After install', body: 'A splash, an arrow to the button, then where to go.', icon: P.steps },
      { kind: 'improved', title: 'One button', body: 'The timer’s pin is its live activity. It morphs.', icon: P.toggle },
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
      { kind: 'new', title: 'Tools', body: 'Citations, a timer, Desmos, a converter, flashcards.', icon: P.tool },
      { kind: 'new', title: 'Pin a tool', body: 'Drag a tool card to the top; it becomes a button.', icon: P.pin },
    ] },
    { version: '2.16.1', date: '2026-09-18', notes: [
      { kind: 'improved', title: 'Tips after setup', body: 'Continue waits a little longer.', icon: P.sparkle },
    ] },
    { version: '2.16.0', date: '2026-09-18', notes: [
      { kind: 'new', title: 'Tips after setup', body: 'Two quick tips replace the tour after setup.', icon: P.sparkle },
    ] },
    { version: '2.15.3', date: '2026-09-18', notes: [
      { kind: 'improved', title: 'Setup', body: 'You pick your courses yourself, then the page reloads.', icon: P.steps },
    ] },
    { version: '2.15.2', date: '2026-09-18', notes: [
      { kind: 'fixed', title: 'Mark as done on pages', body: 'Pages in a module that ask for a mark have the button too.', icon: P.check },
    ] },
    { version: '2.15.1', date: '2026-09-18', notes: [
      { kind: 'new', title: 'Preview attachments', body: 'Attached files have a Preview button.', icon: P.eye },
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
      { kind: 'new', title: 'Away Refresh', body: 'Back after a while? The page refreshes in 3 seconds.', icon: P.clock },
      { kind: 'improved', title: 'Cleaner Persistent', body: 'The extra text under it is gone.', icon: P.toggle },
    ] },
    { version: '2.13.3', date: '2026-09-17', notes: [
      { kind: 'improved', title: 'Simpler What’s new', body: 'Just a list of changes and one button.', icon: P.sparkle },
      { kind: 'fixed', title: 'Shows once', body: 'Skipped a few updates? You see one screen, once.', icon: P.layers },
    ] },
    { version: '2.13.2', date: '2026-09-17', notes: [
      { kind: 'improved', title: 'A smaller switch', body: 'Hover to see the name and Persistent.', icon: P.toggle },
    ] },
    { version: '2.13.1', date: '2026-09-17', notes: [
      { kind: 'fixed', title: 'Blue dot fixed', body: 'The dot after “Simpl” is in the right spot on Windows.', icon: P.sparkle },
    ] },
    { version: '2.13.0', date: '2026-09-17', notes: [
      { kind: 'new', title: 'Persistent on hover', body: 'Hover the top-right switch to turn Persistent on or off.', icon: P.toggle },
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
