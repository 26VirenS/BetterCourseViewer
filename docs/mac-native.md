# Simpl for Mac (native)

Simpl for Mac is a Mac app of its own, not the Safari extension's container app (that one is `macos/`, see
[mac-app.md](mac-app.md)). It is built the way the iPhone app is: the student's own Canvas runs in a web view with the
extension injected into it, signed in on the school's real sign-in page, and that web view is the **engine underneath**.
Over it, the window is the Mac's own: a sidebar, a unified toolbar with Back, Forward and Search, the menu bar, sheets,
popovers, context menus, Quick Look, a Settings window and a window for each external tool. Every number on screen is
asked of the page (`BCVNative.call`, `extension/content/app/native-app.js`), exactly as on the iPhone, so the Mac, the
iPhone and the web count the same things the same way.

Since 2.99.33 the page also **writes** what the apps show, once, for the Mac, Windows and the iPhone alike: every
percentage to one decimal rounded half up (`pctText`, "81.3%"; a ring's whole percent `pctWhole`, `scoreWhole`, and a
letter or a grade's colour band, worked out from that shown figure), the term GPA to two (`gpaText` "3.43", with the
goal's gap worked out from that figure, `goalLine`), letters with the real minus sign, every date and time one way ("Oct
12 at 11:59 PM", the year only when it is not this year, `dueLine`, `factsLine`; Canvas's own lock notes rewritten the
same way, "12am" as "12:00 AM"), each Dashboard counter in a name every tile fits and a full one (`label` "Unread" with
`note` "announcements · 2 courses", `fullLabel` "Announcements" with `fullNote` "3 unread · 2 courses"), a course by the
sidebar's name with a second name only when it says something new — neither the same name
in other punctuation nor a part of it (`altName`, `detail`) —, a chip only for what is out of the ordinary (no
plain "Not submitted"), one graded count for the Grades overview and a course's page (`gradedText`), What-If starting
from the school's own total (`estimateNote` when Simpl's reading differs), a rubric mark that is the points given
(`markText`), the words around a quiz's blank (`around`), and a file sent with an Inbox message (`messageUpload`, then
`attachments`). The apps show these strings as they come rather than formatting the numbers themselves.

It is named **Simpl** with the bundle id `com.simplcourses.mac`, so it never collides with the Safari container app
(`Simpl Courses`) on the same Mac.

## Layout

```
mac/
  Simpl.xcodeproj               written by scripts/dev/make-mac-project.py (CI checks it is in step)
  Support/Info.plist            the app's plist (simpl:// links, local networking for the mock)
  Simpl/                        a synchronized folder: a file added here is in the app, no project edit
    App/SimplApp.swift          the scenes: the main window, a window per tool, Settings; the app delegate
    Engine/Engine.swift         the web view as the engine: calls, where the window is, opening any Canvas address
    Engine/Navigator.swift      places in the sidebar and a browser's Back/Forward history across them
    Engine/FilePreview.swift    a Canvas file fetched with the session, shown in Quick Look
    Engine/AnswerCache.swift    each read's last answer, sealed on disk, for a fast start
    Engine/ReadAhead.swift      this session's answers in memory (AnswerMemo) and reading ahead of the student
    Shell/RootView.swift        the engine underneath, the window over it, sign-in, sheets, Quick Look
    Shell/MainShell.swift       the split view: the sidebar (places, courses and their sections, groups, the account)
    Shell/MoreFromCanvas.swift  the sidebar's More from Canvas (the school's tools, History, Help) and its popups
    Shell/Commands.swift        the menu bar: New Task / New Message, Reload, the Go menu, Change School, What's New
    Shell/LoginLayer.swift      the sign-in card, "Logging you in", "Stay logged in?", Start Over
    Shell/SchoolPicker.swift    the first run: search for the school by name (↑ ↓ Return, as Spotlight)
    Shell/SettingsView.swift    Simpl ▸ Settings: General, Notifications, Grades, Data, Updates, About
    Shell/ToolWindow.swift      an external tool (or a page Simpl does not draw) in a window of its own
    Shell/WhatsNewSheet.swift   what changed, after an update
    Shell/FloatingPopup.swift   the popups over a window that never block it (a tool, Alta, Hand In, What's New…)
    Shell/PageMotion.swift      how a screen arrives and leaves as the window moves (push, Back, a place picked)
    Screens/                    the native screens (Dashboard, Courses, To Do, Calendar, Grades, Notifications, Inbox,
                                Groups, Search, a course's home and sections, an assignment, a discussion, a quiz, setup)
    Components/                 cards, rows, rings, chips, the loader, Canvas's rich text
    Support/Platform.swift      the theme, the house springs, what the shared code expects of the app
    Support/Shot.swift          the screenshot suite's camera (does nothing without its launch argument)
    Support/OpenClock.swift     every screen's open timed on the console, and the screenshot suite's walk
    Support/Updater.swift       updates: the feed, the download and its checks, the swap and relaunch, earlier versions
    Support/Vault.swift         what Simpl keeps, encrypted, its key in the login Keychain (Data and encryption)
    Support/PhoneServer.swift   Send from Phone's server on this Mac's Wi-Fi, up only while its panel is open
    Screens/PhoneHandIn.swift   Send from Phone's panel, its QR code and the photos made into one PDF
    Resources/PhoneHandIn.html  the page the phone opens (the Windows app carries the same file)
```

The web layer (`ios/SimplCourses/Web/`), the models the page's answers decode into, the router, the quiz's run, the
reminders and the new-activity alerts are the **iPhone app's own files**, built for both platforms (`#if os(iOS)` where
they differ: a Mac's alerts are `NSAlert` sheets, its menus `NSMenu`, its file chooser `NSOpenPanel`, its user agent a
Mac's Safari). `scripts/dev/make-mac-project.py` lists them (`SHARED`); run it after adding one.

## Design

- **The Simpl layout in the Mac's own controls.** A quiet page with cards lifted off it (`Theme`), small-capital card
  headings, the course colours, grade rings that fill once — drawn with native buttons, toggles, pickers, menus, steppers,
  date pickers, popovers, sheets and context menus. The sidebar is the Mac's (vibrancy, selection, badges); each course in
  it opens to its sections. (1.3.102) A row pressed again while its place shows a screen pushed on it — an assignment
  opened on the Dashboard, a conversation on the Inbox — goes back to that place's root, as Back goes and one step in
  the history (`Engine.rowPressed`, from a click on the row seen beside the list's own: `PressedAgain` in
  Shell/MainShell.swift; the list saying its selection again as its rows change, or the arrow keys, never do it; a
  course's chevron only folds its sections).
- **Motion.** Springs only, critically damped (`Motion.snappy`, `gentle`, `hover`, `fill`, `page`): they start from what
  is on screen, so anything can be interrupted and turned around. Cards lift under the pointer and give under a click; a
  place cross-fades in; a page opened deeper comes in from the trailing edge a short way and Back
  reverses it (1.3.78, below); a tick shows before its row moves. Under Reduce Motion, moves become cross-fades.
- **Keyboard.** ⌘1–6 the places, ⌘[ ⌘] Back and Forward, ⌘N a task, ⇧⌘N a message, ⌘R reload, ⌘, Settings; arrows and
  Return in lists and pickers; ⌘Return sends.
- **The first run and Settings, after the UI review (1.3.78, the onboarding findings; the same on Windows).** The
  school search lists one row an address (`SchoolSearch.merged`, shared with the iPhone): the best-matching name, the
  others it is listed under beside the address ("also: …"); the school chosen is kept by name for its address
  (`SchoolNames`), so the sign-in card says "Sign in to <school>" with the sign-in page's host under it, offers "Sign In
  on My School's Own Page" and "Not your school? Change School…", and is a shade lighter than the page in dark. "Is this
  your school?" shows white only under a loaded page; loading or failed, a smaller tile in the card's colour ("Couldn't
  load a preview. You can still sign in."). "Logging you in" becomes "Still signing in…" with Open Sign-In Page. The
  setup has no welcome of its own: it opens on "Which courses should Simpl show?" (every course ticked on a first run,
  one line a course — the sidebar's name and the engine's `sub`), with Set Up Later on the first run (`SetupLater`: not
  put up again at launch). The tour is offered at the window's foot ("Take a 1-minute tour?", Not Now counts as seen;
  `-SimplOpen touroffer` for the pictures) rather than begun on its own. Settings: one name a thing ("Courses shown and
  goals", "Keep grade history", "Term GPA goal"), no header repeating its tab, Save Sign-In… when nothing is saved,
  plain footnotes, one Version row with the interface's version in it, the goal half up on its decimal value, a
  drawing pop-up that shows what a place wears, the colour wheel's names at 11 points ("Shade") with a Hex field under
  it, and panes 620 points tall.
- **The Dashboard's counters (1.3.78; 1.3.79: 1.3.77's tiles again; 1.3.80: 1.3.77's heights, rows and grow again, and
  each in its colour).** Each counter is 1.3.77's tile: its symbol in its colour beside its name at the top, its number
  large under them, and its line under that, over its photo — as tall as what is on it (about 110 points), every tile
  in a row as tall as the row's tallest (the squares of 1.3.79 are gone). They stand six across where the page is 900
  points wide or wider (a 1024-point window with the sidebar folded away, or a 1280-point one with it out), else three
  across (two on a page under 520 points), as 1.3.77 had them (`DashboardView.counters`; the window-wide row of six of
  1.3.78, which made 96-point tiles beside the sidebar, is gone). Each row shows every full name where all fit beside
  their symbols, else every short one, never a name cut (a tile too narrow for even those puts each symbol over its
  name). A tile's line is shown whole, in up to two lines, or left out — measured as the tile wraps it
  (`DashCounterTile.noteFits`), never cut in the middle of a word, a sum such as "19 / 49" held together — and its
  tooltip always says it ("4 due today · 35 points total"). In a row of six the line takes one line, so the row stays
  1.3.77's height (about 113 points): a short name's line that needs two gives way to its full name's where that fits
  on one ("Graded" over "19 / 49 points", not "this week · 19 / 49 points"), else it is left out. Every tile wears a soft wash of its own colour over the whole tile, over its photo and under its
  words, and a faint edge in it (`DashCounterTile.wash`: the colour its symbol wears — Due today the accent, Tomorrow
  #ff9f0a, Next 7 days #34c759, Overdue the system red, Unread #ff9500, Graded #5856d6, or the colour worn — at 12% by
  day and 18% by night), drawn by the tile and not as the glass's tint, which macOS mutes in a window in the
  background; Overdue with work in it wears the stronger red wash and a red edge, and its number is red. Pressed, a
  tile grows where it stands into its panel, over the page (matchedGeometryEffect, `dashMorph`, on the house's gentle
  spring): the first tile's panel from its left edge, the last one's from its right, one between them in step, each
  kept on the page and in the window; nothing under it moves. Its ✕, Escape, the tile pressed again (the panel's head,
  where the tile was) or a click anywhere else in the app folds it back into its tile — that click still does what it
  does, so the panel never blocks the app; with one of its rows' cards up, Escape folds the card back first and the next
  closes the panel. The panel is laid out and measured at its own height, never squeezed into the one it was last
  given, so it always shows its whole header and list. Under Reduce Motion nothing grows, its height either: the panel
  fades in over its tile as the tile fades out under it. (The compact strip of 1.3.78 and the popup over the window of
  1.3.79, which faded in, are gone.)
- **The Dashboard and the window, after the UI review (the home findings; the same on Windows).** The view (Cards,
  List, Activity) and a labelled New Task sit in the page by the greeting, so a tool pinned to the toolbar never pushes
  them into its » menu; on a one-column page the view chosen comes straight under the counters. Each row of tiles shows
  every counter's full name ("Announcements" with "3 unread · 2 courses", "Graded this week") where every one fits as
  the tile draws it, else every short one with the engine's note ("Unread", "announcements · 2 courses") — never a mix,
  never a name cut; tooltips and VoiceOver say the count in words ("3 unread announcements"). Red is for late work only:
  Overdue with work in it wears a stronger red wash and a red edge of its own (not the glass's tint, which macOS mutes
  in a window in the background), Due today the accent. While the page shows what was kept from last time, the numbers stay, faint, and
  the date line says "Updating…". (1.3.79) A counter's panel is 1.3.77's again: its symbol, name and number with its
  line beside the number and its ✕ at the right; a row is its course's colour, its title and the engine's line under
  it (course, points, due time) with a chevron or Dismiss, three and a half rows before it scrolls (less where the
  window has less room); a row pressed brings its card up over the window, just under the row or over it where there
  is no room under, never opened inside the panel; a click on the card is the card's, and closing the panel closes
  its row's card. (1.3.80) Where the panel and the card are, for the click that folds the panel back, is read from
  AppKit (`TourFrames`), not from SwiftUI's global frame, which stood 20 points off. The card says what the work's own
  facts say once they are read — never the row's line again under them ("Due 11:59 PM" under "Due Oct 14 at 11:59
  PM"). Neither blocks the window. Course cards:
  a 48-point band with the sidebar's name, the term and teacher under it, "2 new" announcements, quick links named where
  the card is wide. A colour worn marks the sidebar's chosen row's glyph and the controls — never the words or the
  titles — and a drawn scene stays faint behind the sidebar's rows; (1.3.79) a counter's tile wears its photo whole
  again, as in 1.3.77. Typing in Search answers in its suggestions only; the page changes on Return (a sum's Return
  copies its answer). The tour is six steps (the counters, Grades, a course, Tools, Search, then Report a Bug at the
  account menu), with Skip Tour, ✕ and Escape, a Next that always works, and the whole window dimmed, toolbar too, its
  buttons resting meanwhile. A quick look dims nothing behind it and catches no click round it (1.3.79: popups never
  block the window).
- **Grades, To Do, Calendar, Notifications, the Inbox and the rubric (1.3.78).** Grades shows the engine's figures as
  they come (`gpaText`, `pctText`, `gradedText`; the goal's gap from the GPA as shown, so the two add up; the stepper's
  own goal written half up, `HalfUp` in `GradesView.swift`), says why a course has no score ("Hidden by instructor",
  "No grade yet") where its letter would be, names a course once (`altName`), keeps the GPA and Goal cards 540 points
  wide on a wide window, and puts two course cards side by side only where each has 340 points. To Do leads with
  what is left ("17 left", or "1 overdue" in red) under its ring, keeps the flag's slot only where a row shows a
  priority, and has New Task once (the toolbar's +). The Calendar keeps Month / Week / List in the toolbar and ‹ Today
  › beside the period's title on the page (none in List); a month draws only the weeks it touches, its days taller in
  a five-week month; a day lists its open items first (a name on two lines where there is room) and folds work handed
  in into one line ("2 more · 3 done"); only handed-in or excused work is struck through, anything over is greyed
  (`struck`, `past`). Notifications says a row's kind by its symbol (its word is the tooltip) and has Mark All Read and
  Clear All once (the toolbar's). The Inbox names every mailbox where the list has room, and New Message and a reply
  take files (Attach, or dropped on New Message): each is uploaded to the student's conversation-attachments folder as
  it is picked (`messageUpload`) and goes with the message by its id. The rubric is modal: the window under it rests
  (its toolbar's buttons and title too); the ring has a gap between criteria and its names are chips; inside a
  criterion, "‹ Ring" goes back, ‹ 1 of 2 › switches, the levels stack under the description (all of it where there
  is room) with a leader to their points on the bar, and a mark that is not its level's points says so ("Your mark ·
  4 pts", the dot at 4).

- **Knewton Alta (1.3.17, Mac only; cut back in 1.3.45).** Open Tool on a Knewton Alta assignment (the engine's
  `alta`: its launch address, or a title naming it) opens a popup as a quiz does (`Screens/AltaQuiz.swift`): Close and
  the assignment's name, due date and Practice across the top; down the left, its objectives as the quiz's question
  tiles (the one on screen filled, a mastered one ticked) with each by name and its own bar, the total mastery bar, and
  Alta's estimate of the questions the current objective takes; in the rest, Alta's own page, as it is, signed in
  through Canvas — the student works on it there. `Engine/AltaSession.swift` only reads: a script in every frame of
  the popup's page (`AltaHook`) reads a copy of Alta's own answers as its page receives them (the assignment, its
  objectives, each one's progress target, the objective on screen; never the question, the answer key or who the
  student is), the overview's words ("Estimated 4 - 9 questions" and the objective under each) and Alta's own mastery
  bars. Objective names missing from Alta's answers come from the overview (kept per assignment), by id or by place.
  1.3.17–1.3.44 also drew each question in the popup, pressed Alta's Check and Continue, read its verdicts, picked the
  question out of Alta's page and restyled it; 1.3.45 took all of that out (the releases stay up for a rollback).
  (1.3.46) *Save Page Code* (top right) writes every bit of Alta's page code to one text file in Downloads: for the page
  and each frame that ran the page script (each says so with a `frame` message), its HTML from `outerHTML`, each open
  web component's own tree, the text of every `script[src]` and stylesheet it loads (fetched by the page; one that
  cannot be read says why) and any style rules a script put in by hand. Addresses are written without their queries.
  (1.3.47) Alta's question alone, built on Alta's learn page as a saved page's code shows it: one
  `section.compound__compound` in `.learn-flow-body`, a card headed "Question" with what it asks, Learnosity's answer
  box and Alta's Feedback / More Instruction / Submit. When the page is exactly that, the hook marks the section
  (`data-simpl-keep`), its ancestors (`data-simpl-path`), Alta's pop-ups and MathJax's helpers, and a style sheet hides
  everything else (`html[data-simpl-q]`); Feedback and Content attribution are hidden too. Nothing is moved or
  restyled, and the marks change only when what is kept does, so typing and scrolling are untouched. The page is read
  with the marks lifted for that moment, so a part the focus hides is still seen. Anything else — a part of another
  kind, two parts, no part — gives Alta's page back as it is after a moment (1.5 s after a question, 8 s otherwise)
  and posts why with the page's makeup (parts' titles, classes, buttons' words; never the question), which the popup
  keeps as *Error Logs* (top right, shown once there is one), to copy or save with the page code. The overview (Start,
  "Estimated … questions") is shown as it is without a log. `/mock-alta/…/assignment/a3` is that learn page.
  (1.3.48) A lesson (More Instruction) is a part of the same kind, headed with the lesson's name, with Feedback and
  Continue: every part of the learn page is now kept — a question, a lesson, or a lesson above the question — so
  Alta's header stays hidden through lessons; only a learn page with no part (or a page that is not one) is given back
  as it is.
  (1.3.49) The question (or lesson) is laid on Liquid Glass and still drawn wholly by Alta: while it is shown alone, the
  focus style sheet clears the ground and shadow of the page, of every ancestor of the parts kept and of each part's
  card, and the web view draws no background of its own, so the popup's glass panel (`glassCard`, radius 22, light even
  in dark mode — as Alta's dark text and white answer boxes need — and whiter there) is the card. Nothing inside the part
  is restyled: its words, MathJax, Learnosity's answer box and keypad, Alta's buttons. Alta's page as it is (not
  recognised) is shown edge to edge on white, with its own ground.
  (1.3.50) The glass is the app's own, light or dark. In dark mode each kept part is turned over by a filter —
  `invert(1) hue-rotate(180deg)`: dark words on light become light on dark, hues kept — and its pictures, videos,
  canvases and frames are turned back, so Alta still draws everything and only the colours change. The app tells the page
  through `window.__simplDark`: a script ahead of the page script on each page (`appearance(dark:)` re-adds the user
  scripts), and at once to the page shown (`__simplFocusDark()` sets `html[data-simpl-dark]`).
  (1.3.51) Alta's pop-ups as the Mac's own: the hook passes each open `[role=dialog]` (its title, words and buttons) to
  the app, which takes a picture of it as Alta draws it (`takeSnapshot` of its rect, the pop-up shown for that moment),
  hides it in the page (`html[data-simpl-native-dialog] [data-simpl-dialog]`) and shows a sheet with that picture and
  the pop-up's buttons, each pressing Alta's (`__simplDialog.press`); Escape presses its Close / Got it / OK, else gives
  the pop-up back to the page. A changed pop-up (a next step) is pictured again. The page's sprite sheets (an `<svg>` of
  symbols) are never hidden by the focus: a pop-up's picture draws from one.
  (1.3.54) A pop-up's picture leaves out its buttons (the sheet has them): hidden for that moment
  (`html[data-simpl-dialog-shot]`) and the row of them at its foot cropped off (`__simplDialog.rect`).
  (1.3.83) Pop-ups read whole. The picture came out empty when a pop-up opened while Alta's page was still out of sight
  (its learn page loading, or between parts: `body` see-through), so the sheet showed a title and a button round
  nothing; and once the app hid it, its words read as nothing and it was passed again. Now the hook finds a pop-up
  (`[role=dialog]`, `[role=alertdialog]`, `dialog[open]`, `[aria-modal=true]`, Material's, React Toolbox's and
  react-modal's boxes, in web components' trees too; the innermost, the last), reads it with the app's hiding lifted,
  and passes it once it has settled — 350 ms unchanged, no spinner in it, its pictures loaded, its fade done; three
  seconds at most — and again when it changes (a next step): its title (`aria-labelledby`, `aria-label`, else the
  heading it opens with), its body as plain HTML (paragraphs, headings, lists, tables, bold and italic as Alta's styles
  make them, links as `simpl-link:N`; each formula as TeX — MathJax 2's script, MathJax 3's record, KaTeX's or MathML's
  annotation, `data-latex` — else as MathML; a picture by its address, a drawing as SVG with the symbols it uses, a
  canvas as its picture) and every button (`label`, `primary`, `enabled`, `close` for an ×). The card (1.3.84: inside
  the popup, below) draws the body with `HTMLBlock` (KaTeX on; its plain words until it has measured itself; a scroll
  once it is as tall as the popup leaves room for), presses Alta's
  buttons and follows its links in Alta's page (another site's, or a new window's, in the browser), and hides Alta's
  own only once it is up (`__simplDialog.hide`). A pop-up holding a video, a form, another site's frame, or a picture
  or formula that cannot be read, one with nothing to read, or one still loading after three seconds is never an empty
  sheet: it is given back to the page (`kind: 'dialog', inPage`, with why, for the Error Logs), as is one the student
  sends there (Show in Alta's Page, Escape with no Close): the popup shows Alta's page as it is — revealed, the focus
  lifted, edge to edge (`AltaSession.asIs`) — until it closes. Alta's pop-ups are kept in sight by the focus whatever
  the page is doing (`keepDialogs`: one that came between parts was hidden with the page). In the mock's learn page
  `mockWelcomeDialog(ms)` opens a slow, rich, two-step one (`?sample=welcome` opens it by itself: the pictures'
  `21m-alta-dialog`; `?sample=video` the video one, `21n-alta-dialog-in-page` — (1.3.85) the Windows app's pictures
  ask with `?sample=` too, `1` and `dialog`, for the rich pop-up of its own, `mockRichDialog`), and `mockVideoDialog`,
  `mockFrameDialog` and `mockBareDialog` ones the page keeps.
  (1.3.84) The pop-up is a card of the popup's own over its content (`AltaDialogLayer`): the popup dimmed under it and
  resting, held open (Escape and ⌘W answer the pop-up, the bar's Close gives up Escape meanwhile), nothing outside the
  popup dimmed. Through 1.3.83 it was a SwiftUI `.sheet` begun from a view in the popup — a borderless child panel over
  the window (`PopupPanel`), no window a sheet hangs from as it should — and the 1.3.84 pictures showed the main window
  dimmed as under a sheet, with neither the popup nor its pop-up in sight.
  (1.3.83) *Light or Dark* above the objectives (`AltaLookPicker`: two glass chips at the rail's head, the one picked
  filled): Alta's side of the popup light — 1.3.79's, the default — or dark,
  remembered for every Alta assignment (`SimplAltaPageDark`; `-SimplAltaPageDark YES` for the pictures). Dark is 1.3.78's
  look again: the page region in a dark environment (dark glass panels, the start screen, the skeleton), the web view's
  appearance dark, `__simplDark` told to the page so each kept part is turned over (1.3.50), the graphs' own Light or
  Dark beside them and the graph window dark (1.3.70); and Alta's page as it is — not recognised, its overview, or
  under a pop-up given back to it — drawn dark as a tool's page is (`html[data-simpl-whole-dark]`: `invert(.92)
  hue-rotate(180deg)`, its pictures, videos and frames turned back), unless it has a dark look of its own (a dark ground,
  or a `color-scheme` meta naming dark), which the hook tells once the page is in sight and out of the focus. The bar
  and the rail keep the app's appearance.
  (1.3.83) *Show Browser* beside them (`AltaBrowserToggle`, a glass chip filled while on; the row says "Browser", then
  drops the sun's and moon's words, where the rail is narrow): Knewton Alta's own page on the right — the same web view,
  so the same assignment, signed in, where the student is in it — as Alta draws it, in place of the app's view; off, the
  app's view again. Remembered (`SimplAltaShowBrowser`; `-SimplAltaShowBrowser YES`). The app tells the page
  `window.__simplBrowser` (with the other flags, before each page and at once: `AltaSession.tellPage`), and the hook
  stands down while it is on — no focus (`unmark`), no veil (never out of sight, not even between parts), no panels or
  buttons for the app, its pop-ups its own (one the app was showing given back) — while it still reads the objectives,
  the mastery and the cover for the rail. The popup shows the page as it is (`AltaSession.asIs`), and Light or Dark
  applies (Dark draws it dark as above).
  (1.3.53) Two glass panels, the question's words and its answer (a lesson: one): the hook tells the app each kept
  part's question and answer boxes (the card's children before and from the one holding Learnosity's box, which gets a
  gap above it — `data-simpl-answer`) in the page's own coordinates, and the page's height. The app sizes the page to
  that height inside its own scroll view, a native glass panel behind each box, and the page hands its scrolling to the
  popup (`AltaWebView.forwardsScroll`); a new question scrolls back to the top. Every button in the parts' button bars
  (`[class*=button-bar]`), whatever it says, goes to the app — its words, Alta's main one (`primary` in its class),
  whether it can be pressed — and the bars are put away (`data-simpl-bar`): the app shows them as its own glass buttons
  (the main one prominent, ⌘↩) and presses Alta's (`__simplButtons.press`). A sprite sheet kept for its symbols makes
  the parts round it that hold nothing else kept take no room (`data-simpl-thin`): Alta's banner held one.
  (1.3.99) Alta's verdict on the question — its green "Yes that's right. Keep it up!" strip, its red "Not quite…" — is
  read from the kept parts (`verdict` in the panels message: a banner, an alert or a box marked by its class, told by
  its words first, else by its class) and shown as the popup's own pill at the left of its buttons, Correct in green or
  Incorrect in red (`AltaVerdictPill`), until the strip goes with the next question.
  (1.3.101) Faster to open. The launch is asked of Canvas the moment the popup is asked for (`Engine.alta`'s `didSet`
  makes the `AltaSession` and starts it; the popup takes it with `altaSession(for:)`), not once its view is built; the
  header's due line comes with the launch (`AltaLaunch.dueLine`, from the Assignment page or the Tools row), the
  `assignment` call only when it did not, after the launch. The skeleton is up from the first moment and stays until the
  first page of Alta's own (the hook's `frame` says `alta`) — no spinner first, no Canvas launch page in sight. The hook
  shows a part once nothing in it is loading and nothing in it changed for 100 ms (350 ms of a page with nothing
  changed anywhere, before); a question only once Learnosity's widget is drawn in it, and maths only once set (MathJax
  2: each formula's script has its drawing beside it; MathJax 3: its typeset promise settled, no `\(`/`\[` left);
  changes in Alta's chrome round it no longer count, never put off the next look at the page for more than 300 ms, and
  once a part is kept have the page looked at no oftener than every 400 ms; capped at 1.5 s (2.5 s while something in
  it is still drawn, 4 s while it says it is loading), from the first look and never lowered by a later one, so a
  Learnosity box or picture that comes in after 1.5 s is still given its 100 ms rather than shown at once. The Tools list asks the launch as its row is chosen
  (`Engine.askAlta`), while the list closes. A launch answered with Canvas's own assignment page (no sessionless
  launch) is shown as soon as it loads; any other page that is not Alta's after 2.5 s still there; a popup closed
  before the launch is answered never loads it. Each open writes one line on the console — `[Simpl] alta: asked 2 ms · launch 410 ms · page 655 ms ·
  question 790 ms · shown 905 ms (question, quiet 120 ms, 118 ms after DOMContentLoaded)` — from the hook's own clock
  (`t`, `nav` on its posts; `quiet`, `capped`, `dcl` on `shown`).
  (Maths drawn whole.) A part of Alta's is never hidden while it is drawn. The focus hid everything not yet marked with
  `display: none`, the next question too, from the moment Alta put it in until the hook next looked at the page
  (120–420 ms), and Learnosity's answer box (MathQuill) sizes its brackets and √ once, as it draws them, from what they
  hold: drawn hidden, they came out flat (`scale(0.8, 0)`, `scale(1, -0.1)`) — no brackets, no √, only the bar over
  the radicand — and stayed so, Light, Dark and Show Browser alike, until a relaunch. Now a part not kept yet is laid
  out and only see-through (`opacity: 0`) until it is kept — out of the flow (`position: absolute`, as wide as the flow
  it is in), so a lesson Alta puts in above the question never pushes the question down under its glass meanwhile — and
  what holds a part put in is marked `data-simpl-path` in the same moment (`layOut`, from the MutationObserver). A bracket or √ found flat anyway (Alta's own hiding) is sized
  again as MathQuill sizes it (its `reflow` where the page has MathQuill to hand; `remeasure`, every 350 ms and before
  the page is shown, in Show Browser too), said in the Error Logs. The page is never left out of sight either: a part
  shown or put away by its class or style alone has the page looked at again (an attribute observer on the learn flow,
  its parts and what holds it), between parts it is looked at every 300 ms (it waited out the 8 s grace before), and a
  kept part gone or out of sight, or the page out of sight with nothing due, has it looked at again at once (`watch`) —
  what Light then Dark did by hand, through `tellPage`'s `__simplBrowserNow`. The popup's own last resort
  (`watchSkeleton`): its skeleton still up over Alta's page asks the page to look again every 4 s from 6 s, and after
  26 s has the hook show it as it is (`__simplAsIsNow`: its waits counted as over, the focus lifted, why said in the
  Error Logs with what the page held), so the hook's `showing` stays in step and the page can still be put out of sight
  between parts after. Once a grace between parts has run out, the page is no longer put out of sight again at each look.
  (1.3.106) A web view made ahead. For an account that uses Alta (`Engine.hasAlta`: the Tools list named Knewton Alta,
  an assignment opened it, or one was opened — kept per school, forgotten on sign-out) one web view for the popup is
  made ahead (`AltaSpare`): at a quiet moment once the Dashboard is up, when an Alta assignment's page is read, and
  after each Alta popup closes — never while one opens. It holds only an empty page (about:blank), so WebKit's process
  for it is running when the popup takes it (`take`, by school); one that has shown anything is never handed on, and it
  is let go after ten minutes unused, on sign-out and when another school is chosen (Change School…). The console's
  line says which: `alta: view spare · …` or `alta: view made 38 ms · …`; `-SimplAltaSpare YES` opens a picture's
  popup in one (`21w-alta-spare`).
  (1.3.106) A lighter page script, saying what it said before. The panels, buttons and verdict are told when something
  may have moved them — a change in the parts kept (their elements and words; a class, a style, a button greyed or not;
  a fade ending; in the answer box itself only a verdict or the buttons' bar, never the caret blinking in a math box),
  a part's size or the window's — at the next frame, no oftener than every 200 ms, again 450 ms after a
  change in the parts, and every second whatever happens (1.3.53–1.3.101: every 350 ms always); the verdict is read
  again only after a change in the parts. The page is read afresh (its marks lifted) only for a change in the parts or
  Alta's learn flow, or every 2 s; web components are listed once and looked for again only when the page gains an
  element or a tree; keyword tests read `textContent` before `innerText`; and the mastery bars, the overview's words and
  the cover are read only once the page has been shown, its cover read, or 3 s have passed — while the question is
  shown alone and nothing changed, only every third time.
  (1.3.106) The mock's Alta launch can go as Canvas's does (`--alta-launch`, as `alta-test.mjs` runs it): Canvas's own
  page, whose form sends itself on to Alta (POSTed), then Alta's page; and its Alta content answer comes 500 ms late.
  `scripts/dev/alta-test.mjs` drives the mock's Alta player (`/mock-alta/…`) through it in Chromium; the popup is shot
  as `21a-alta`.
- **Quizzes (1.3.32).** The quiz's start screen lists every finished attempt, newest first, with its score (when Canvas
  shows results) and when it was handed in, the one Canvas keeps by the quiz's scoring rule marked Kept (`quizIntro`'s
  `attempts`, from the quiz assignment's `submission_history`). A quiz's card preview reads the quiz's own assignment
  (`quizAssignment`), so a graded quiz shows its score wherever its card is, not only "Graded".
- **Assignments, quizzes and the publishers' popups (1.3.78, the UI pass).** Each fact is said once, in the words and
  figures the engine writes (its 2.99.33 strings). An assignment page in one column reads in order: its
  next step, the facts in a line under it (`factsLine`, "Due Sep 30 at 10:30 AM · 16 pts · 1 of 2 attempts used"), the
  grade once there is one, then the instructions; two columns keep the facts card. The rubric is a row under the name in
  one column and a button on the grade card in two, never both. The toolbar's copy of the next step shows only once the
  page's own button has scrolled out of sight (macOS 15 and later). A quiz's attempts come from its intro wherever it is
  read (the facts' Attempts, the line, New Attempt), and New Attempt keeps its place, greyed with a spinner, until the
  intro answers. The grade's ring and letter take the grade's colour band (`Components/GradeBand.swift`: A green, B
  teal, C yellow, D orange, F red — the Grades screen's), its figures the engine's (`pctText`, `pctWhole`). In a quiz:
  the intro is the quiz's facts and one attempts line (the bar names the quiz and its course); Begin Attempt keeps its
  name, greyed until an access code is typed; a question's plain words are drawn by the Mac itself (`QuizStemText`:
  paragraphs, marks, links, the blanks' boxes holding what was picked or typed) and Canvas's rich text is kept for
  pictures, formulas and tables, the answers held back until it has its height (at most 0.6 s), so a question's answers
  never show before its words; answer rows about 46 points tall, the number key shown on the row under the pointer;
  "Select all that apply" above the boxes; each blank's field says the words around it ("the ___ of"); the count of
  answered questions is said once, at the foot, the side column a bar and a one-line key; answered tiles a deeper wash
  with a tick in dark mode; the review's words take the row (up to three lines) above a docked bar; the feedback's ring
  in its grade band, its Back a "‹ Quiz" chevron by the title. Every full popup — the quiz, Knewton Alta, Macmillan, the
  feedback on a file — has Close at its bar's right end. The feedback on a file with no comments has no empty pane: the
  bar says "No comments on this submission". Alta's header writes its due date as every date is written (Canvas's
  `dueLine`, else Alta's own read and written the same way), its start screen shows only what the bar and the rail do
  not (status, grade, the note, Start, and the topics with their estimates), and its mastery before Alta says it is the
  mean of the objectives'. Macmillan's popup is titled with the Canvas assignment's name (else its page's heading) over
  "Macmillan Learning · Due …". The sidebar keeps the row of the place showing in sight (a window more opened on a
  course's Grades shows its row).
- **Send from Phone (1.3.78, the Mac and Windows apps only).** Hand In's *Send from Phone…* (beside Choose Files…, for
  an assignment that takes files) opens a panel over the form with a QR code. The phone's own camera opens it: a page
  this Mac serves itself (`Resources/PhoneHandIn.html`, the same file as the Windows app's), with the assignment, its
  course and the kinds of file it takes, *Take Photo* and *Choose Files*, each file's progress and Retry, then "Sent to
  your Mac — finish handing in there." Each file arrives in the hand-in as if chosen with Choose Files… (its type
  checked, converted where it must be); the student looks it over and presses Submit on the Mac — nothing is handed in
  by itself. **No server anywhere:** `Support/PhoneServer.swift` is a small web server (Network.framework, `NWListener`)
  on this Mac's private Wi-Fi (or Ethernet) address and a random port, up only while the panel is open — it stops when
  the panel closes, when the hand-in is sent or cancelled, and after 15 minutes with nothing from the phone. The
  address holds a 128-bit one-time key (compared in constant time); every other path is 404; only private addresses may
  connect; one file is at most 50 MB (Hand In's own limit) and a session 500 MB; names are made safe; what came is kept
  in a temporary folder (`SimplPhone-…`) that goes with the hand-in. The phone and the Mac must be on the same network,
  and the panel says so in those words: "Your phone needs to be on the same network as this Mac (your phone’s hotspot
  works)." This Mac's network and address are behind *Trouble Connecting?* (some school networks keep devices apart),
  and the whole note goes once the phone has connected; with no address at all the panel says "Connect this Mac to
  Wi-Fi or a network to use your phone." Done, at the panel's foot, is its one way to close. An assignment that takes
  PDFs and not pictures offers *Combine photos into one PDF* under the code, before anything is sent ("Photos become
  one PDF: project01.pdf"): the photos, turned upright and made no larger than 2200 pixels, a page each in the order they
  came, as one PDF named for the assignment; once a photo has come, its row says which page it is. The code (`PhoneQR`: Core Image's generator, correction M, its modules read back) is
  drawn in one Canvas — softly rounded modules, dark on white even in dark mode, four modules of quiet space — and
  arrives once: the panel grows from the button (from 96%, fading in, 240 ms, cubic-bezier(0.23, 1, 0.32, 1)), the
  finder squares settle (from 85%), then the data ripples out from the centre (each module from 60% over 120 ms, all of
  it in place within 420 ms); under Reduce Motion a 150 ms fade. "Waiting for your phone…" has a breathing dot, never
  the code. When the phone opens the page the panel cross-fades to "Phone connected", the code shrinking into its
  corner, and lists each file coming in with its picture. `Info.plist` says why Simpl uses the local network
  (`NSLocalNetworkUsageDescription`). The Chrome and Safari extensions and the iPhone app do not have it (an extension
  cannot take a connection). Pictured as `12c-hand-in-phone` (light and dark) and `12d-hand-in-phone-connected`, where
  `mac-shots.sh` sends a file to the server with `curl` and checks the console for it in the hand-in.

- **The full Periodic Table (1.3.52, Mac only).** `Tools/PeriodicTable.swift`: the table can be coloured by its kind,
  electronegativity, atomic radius, ionization energy, electron affinity, melting or boiling point, density (log scale),
  orbital block or state (`PTColouring`, `Tools/PeriodicModel.swift`), with the scale, each cell's value and, for the
  properties with a simple trend, arrows across a period and up a group. The table's empty top shows the element under
  the pointer only; a press opens the element's page (`Tools/PeriodicDetail.swift`), grown out of its square (a scale
  transition anchored there): its atom in SceneKit (`Tools/Atom3D.swift`: the nucleus's protons and neutrons, at most 72
  drawn, each shell a ring with its electrons going round) or its orbitals (each subshell's, side by side, lobes in two
  phase colours, an empty one faint; the f shapes simplified), all turned and zoomed with the mouse; its facts; how it
  ranks among all elements; its successive ionization energies (log scale, the jump into the core marked); its
  configuration in full with orbital boxes (Hund's rule); where it sits; its neighbours; and its story. ← → step through
  the elements, ⌘[ goes back. The extra data (ionization energies, affinity, radii, shells, CPK colours, appearance,
  molar heat, named by) is `Tools/PeriodicExtraData.swift`, from the same Periodic-Table-JSON. The pin's small table
  shows the element under the pointer and opens the full table on its page when pressed.
  The *Lewis structure maker* (`Tools/LewisMaker.swift`): a central atom, the atoms round it and a charge give the
  structure — single bonds to the centre, outer octets with lone pairs, the rest on the centre, outer lone pairs turned
  into multiple bonds until the centre has its octet (B and Be left short), and from period 3 down bonds added while
  that lowers the formal charges — and from it the electron groups, VSEPR shape and angle, polarity, formal charges and
  octets, drawn on a canvas; *Open in 3D builder* hands it over. The *molecule builder* (`Tools/MoleculeBuilder.swift`):
  atoms added to the selected one with a chosen bond order, hydrogen filled in, presets; shaped as VSEPR has it (laid
  out since 1.3.74, below; forces before); SceneKit with camera control and click-to-select; ball and stick, space
  filling or sticks; lone pairs, labels, turning by itself; its formula (Hill order), molar mass, counts, valence check,
  polarity (from bond dipoles and lone pairs) and the selected atom's shape and bond angles as the model has them.
  (1.3.54) The Lewis builder is a graph (`LGraph`: atoms with positions, bonds with orders, a charge), made by the quick
  way (the old central-atom screen, which builds the graph) or drawn (`LewisEditorCanvas`: click to place an atom bonded
  to the one selected, drag between atoms to bond, click a bond to cycle its order — the orders then the student's own,
  until Auto bonds —, move, erase, tidy): chains, branches, rings, two-atom molecules, any main-group element.
  `LewisSolver` places the electrons for any graph: total valence electrons less the charge; every bond-order assignment
  the atoms allow (each atom's most bonds from its most electrons: 2 for H and He, 4 Be, 6 B and Al, 8 in period 2, 12
  below); outer atoms filled first (the more electronegative first), then inner, then inner past eight, an odd electron
  on the least electronegative atom with room; formal charges; ranked by octets kept (strict octet: none past eight
  either), then the sum of formal charges, like charges side by side, and negative charge on electronegative atoms.
  The best tie are resonance structures (drawn side by side with ↔, their average bond orders listed), close runners-up
  minor contributors. Group 1 and 2 metals with nonmetals are ionic (cations; monoatomic anions to their octets; the rest
  of the charge on the polyatomic anion), each ion in brackets with its charge, as is any charged molecule. Every atom's
  octet, formal charge and VSEPR shape; polarity from the molecule built in 3D (`MoleculeModel.load(graph:structure:)`,
  which the builder's Open in 3D also uses); warnings (period 2 over eight, hydrogen with two bonds, electrons that do not
  fit, unconnected atoms); notes instead of a structure for transition metals and diborane, and beside O₂'s. Formulas
  in conventional order (Hill for carbon compounds; else by electronegativity, H first or after a group 13–15 element).
  The table's colour views and its kinds' chips are glass buttons (the chips tinted their colour), the trend arrows are
  always shown for a property with a trend, and the search sits in the tool's header, left of the pin (`PTSearch`).
  `scripts/dev/mac-shots.sh` pictures the table coloured by electronegativity with its trends, carbon's and iron's pages,
  the Lewis maker and the builder (`-SimplPTColour`, `-SimplPTTrends`, `-SimplPTElement`, `-SimplPTPage`,
  `-SimplPTMolecule`).
- **The biology toolkit (1.3.78).** Ten tools under a *Biology* heading on the Tools page (Cell Explorer, DNA & RNA,
  Gene Expression, Biomolecule Builder, Genetics Crosses, Cell Division, Hardy–Weinberg, Bio Lab Calculator, Enzyme
  Lab, Photosynthesis & Respiration), each pinnable, each in a file of its own in `Tools/Biology/`, on a shared
  foundation: the specification's datasets copied in by a script and parsed on first use, the sequence model, B-DNA
  atom by atom, exact crosses, chi-square, SplitMix64, procedural meshes with a cutaway made in geometry, a SceneKit
  view with picking, hover, selection and labels with leader lines, a step-through with its keys, and §3.8's colours
  (`Tools/Biology/Model` is Foundation only and checked by CI's Models job, with the toolkit's self-check). A launch
  opens a tool in a state for pictures: `-SimplTool cell -SimplToolState plant:cutaway`; `-SimplBioCheck YES` prints
  the self-check. The foundation's API, each tool's files, launch states and hand-offs, and their pictures:
  `docs/mac-biology.md`.
  The *Biomolecule Builder* (`Tools/Biology/BiomoleculeTool.swift` and `Biomol*.swift`; its models in
  `Model/Biomol*.swift`, checked by the Models job as `biomol.*`): five pages sharing one state (`BiomolState`) and the
  chemistry builder's look (CPK colours and radii; ball and stick, space filling, sticks; Colour by element, part or
  class; Show H; the pH 7 form; labels with leader lines). *Amino acids*: the 20 by class with their R groups sketched
  in 2D, each in 3D from its template with its card and the CORN rule, and its D-form mirror image. *Proteins*: a
  sequence (up to 40) whose secondary structure is painted residue by residue, built as §6.4 says (NeRF backbone,
  template side chains, the clash relief, proline's rule; a Pro outside a helix keeps its ring's φ), with backbone
  H-bonds, a cartoon over or instead of the atoms, a residue added or removed playing its dehydration or hydrolysis,
  and the β-sheet demo; and the four levels — beads, a helix beside a sheet, ubiquitin (its core and surface,
  denatured into a seeded random coil by morphing and back) and haemoglobin with its hemes. *Nucleic acids*: the
  nucleotides and ATP, and a DNA strand built nucleotide by nucleotide on the B-DNA model with its phosphodiester bonds.
  *Carbohydrates*: five rings with numbered carbons and α or β read from the atoms, four disaccharides split and joined
  by water, and five polysaccharides as hexagon glyphs. *Lipids*: fatty acids, tristearin, POPC and cholesterol, with
  esters, the cis kink and head and tails. *Open in Molecule Builder* hands the atoms to the Molecule Builder (1.3.80:
  a chemistry tool of its own), which keeps their shape (`MoleculeModel.load(handOff:)`, `fixedShape`: no VSEPR). Launch flags for pictures:
  `-SimplTool biomol -SimplToolState <page>:<options>` (`BiomoleculeTool.apply` lists them).
  (1.3.55) The Lewis and 3D builders' presets are one grouped dropdown each (`PresetMenu`; `LewisPresets.sections`,
  `MoleculeModel.presetSections`) instead of rows of chips, and an element (the central atom, or the one to draw) is a
  card — symbol, name, valence electrons, most it holds — with a Change menu of the main-group elements by group
  (`LElementPicker`). Drawings fit their space (shrunk and centred, held still while the pointer is down), rings start
  as regular polygons, and an ion's charge is drawn in plain digits and signs, full size.
  (1.3.56) An element's page picks its layout from its measured width (wide past 1000 points, a middle width from 620): the atom and its electron configuration side
  by side, as tall as each other, its facts below, instead of the atom stretched across the page.
  (1.3.57) Elsewhere each card keeps its own height (the wide layout no longer stretches the atom's card to the facts'
  column), and every fact box is one size (the line for a note under the value is kept, empty, where there is none).
  The orbitals open on the outer subshell being filled (the highest l among the valence subshells: 3d for cobalt, not
  4s) and the camera stands back far enough for the s sphere or the row of orbitals to sit well inside the view. The
  presets' dropdown and the element card are plain full-width menus (the card itself opens the element menu). The
  Lewis editor draws up to half again full size when there is room (`editorFit`), and the quick way sets the atoms
  round the centre as its shape is drawn on paper, from the lone pairs the electrons give it (`quickAngles`: water
  bent, ammonia a fan under its lone pair, ClF₃ a T, SF₄ a seesaw, XeF₄ a cross).
  (1.3.58) The work layout's side columns hold their content to the column's width (less room for the scroller), the
  Lewis ranking's choices are short (Octet rule, Formal charge) so it fits, and the 3D builder turns by itself from the start.
  (1.3.59) A short tour (`PeriodicTour.swift`) the first time the full table opens, and from its Tour button: a glass
  card at the bottom, a line a step, each step setting the tool to what it shows (the table, a colour view, carbon's
  page, the Lewis builder's quick and drawn ways and NO₃⁻'s resonance through `PTTour`, the 3D builder, search); Return
  for next, Esc to skip, seen once (`simpl.ptTourSeen`). In the 3D builder the selected atom is ringed (a torus that
  faces the viewer) instead of a see-through shell that fought the spheres round it, the symbols sit on each sphere's
  near side and are hidden behind nearer atoms, turning by itself is a button at the viewer's top right, and handling
  the model (drag, scroll, pinch) pauses the turning until three seconds after the last touch. Alta's glass panels all
  take the widest panel's width, so the question's and the answer's edges line up.
  Alta's assignment cover (its page before it is started) is the popup's own start screen on Liquid Glass: the hook
  reads it (`kind: 'cover'`: the name, the header's Due Date, Status and Grade, its note, its Start as Alta words it,
  Review Instruction, and the objectives by topic with each topic's estimate and each objective's mastery) and the
  popup shows a glass card with the name, details, note and a large Start (Return) and Review Instruction, and a glass
  card of the objectives — each Start or Review pressing Alta's own (`window.__simplCover`). Alta's page waits under it.
  (1.3.60) Alta's page is see-through from its first moment (a style sheet adopted at document start: `body` at opacity
  0 until `html[data-simpl-shown]`) until the hook has laid it out — its question on glass, its cover read for the start
  screen, or, not recognised, shown as it is — or nine seconds have passed; a page that is not Alta's is shown at once.
  The popup shows its own Loading meanwhile and no white behind it, so Alta never flashes in before its question. Each
  part of a question is a centred column at most 860 points wide. The cover's objectives (every one, by topic, with its
  topic's estimate) are the assignment's list: the rail's on the start screen, and the names lined up with Alta's answers
  once a question is on, so none is left as "Objective 3". A detail's second line of another kind (Grade's "Last
  updated…") is shown as a note under its value.
  (1.3.61) The tour is a guided one: each step dims the tool and cuts a spotlight round the control it is about (the
  table's carbon, shown as if pointed at; the colour views; an element's 3D atom; the Lewis builder's centre atom, its
  drawing and its resonance; the 3D viewer; the search in the header), ringed, its card beside it pointing at it — the
  hole and the card moving from step to step. Controls name themselves to it with `tourTarget` (their place on screen,
  kept in `PTTourFrames`).
  (1.3.62) The tour is interactive, as the app's own (Shell/Tour.swift): each step asks for one thing — hover an
  element, pick a colour view, open an element, see its orbitals, go to the Lewis builder, choose a preset, draw, click a
  resonance structure, go to the 3D builder, drag the model, search — and moves on by itself once it is done, its card
  the app tour's (where it is, what it is, what to do with its gesture pulsing, a bar for how far, its arrow on the
  card's glass), a Next only if a step is left undone twelve seconds. The pages (Table, Lewis structures, Molecule
  builder), Tour and search are in the tool's header (`PTHeaderControls`, `PTNav`): search a glass button that opens
  into a field (⌘F), the pages drawing in to their symbols to make room; the header shows the table's name alone.
  In dark mode, Alta's graphs and drawings (a graph's box by its name — Learnosity's, JSXGraph's — or an SVG at least
  140 × 100, marked `data-simpl-pic`) are turned back whole, labels and all. Alta's page goes out of sight again
  between parts (the app's Loading) for as long as Alta may take — never shown as it is in the meantime — the page's own
  ground cleared while it is out of sight, and a new page's glass panels and buttons put away at once.
  The Dashboard's opened counter is 440 points wide, and a click anywhere in the app but its panel folds it back.
  (1.3.63) The Lewis builder's steppers are pressable over each button's whole square (a plain button is pressed only
  where it draws, and a minus sign is a thin line).
  (1.3.64) The header's search lists what it finds under the field (up to seven: each element's tile, name, number and
  kind; ↑ ↓ to move, Return or a press to open it, `PTSearch.pick`), over the tool. The tour ticks a step off when it is
  done ("Nice", green) and waits 1.6 seconds (2.2 after the last) before the next.
  (1.3.65) Faster: each colour view's scale, and every element's colour and words under every view, are made once (the
  scale was worked out over all 118 elements for every cell each time the table drew); an element is looked up by its
  number; a cell is drawn again only when its element, size, view or marks change (`PTCell` Equatable); the Lewis
  builder works out a molecule's electrons only when its atoms, bonds, charge or the ranking change, not when it is
  moved about or the builder draws (`LewisMemo`, `LGraph.topologyKey`), and its polarity (a 3D build and 900 steps of
  relaxing) once for each molecule and structure; the 3D builder's picture is built again only when what it is made of
  changes; an element's subshells are worked out once; the tour's controls' places are told only while the tour is on.
  (1.3.66) Macmillan Learning: an assignment whose tool is Macmillan's (Achieve, Sapling, LaunchPad — its address, or a
  title that names it; `macmillan` in the assignment's data, native-app.js) opens in its own popup, as an Alta one does
  (`MacmillanScreen`, `MacmillanSession`): the launch through Canvas, Macmillan's page as it draws it, and Save Page Code
  (`AltaSession.collect`, every frame's code too) — the start of laying it out as the app's own, from its saved code.
  `-SimplOpen macmillan:104:4005` opens the mock's (the screenshot suite's `21d`).
  (1.3.67) External Tools, in the sidebar under Tools (Canvas only): every external tool in the student's current
  courses — each course's own tabs, and the tools found only in its assignments and module items (native-app.js
  `externalTools`: one row a tool, named as the web's search names it, hub.js `toolNameOf`, else by its site) — a card a
  tool with its site, where it is found, and a button for each course it is in, opening it there (by its tab, else an
  assignment, else a module item; Knewton Alta and Macmillan in their own popups). A filter on the page narrows them
  (not a toolbar search field: the window's toolbar has the app's own, and a second one crashed it).
  (1.3.79) A tool is its provider (hub.js `providerOf`, its `PROVIDERS` table: a provider's id, name, icon word, hosts
  and names): every assignment, module item and tab that opens Macmillan Learning — lmslink.bfwpub.com, Achieve, Sapling
  — is one card, "Macmillan Learning", each place under its own title ("W2: Atoms and Elements", "W7 Homework") in its
  list; before, a title's words before its colon named a tool of their own ("W2", "W2 HW", "W7 Homework"…, a card each).
  A provider is found by its launch address's host; else, where Canvas gives the tool's id (a tab's own, an assignment's
  or a module item's `content_id`), by what the same tool's other places say — so a tab the school named "Course
  Materials" joins Macmillan's card; else by a tab's label or an item's title that is a provider's name ("Achieve",
  "Knewton Alta: Unit 2", "Mastering Physics"); else by its site ("bfwpub.com"); a tab that names none by its label.
  Never by the words of a title alone. A launch on Canvas's own route (`/courses/1/external_tools/retrieve?url=…`,
  `…/external_tools/31`, `sessionless_launch?id=31`, on the school's Canvas or `school.instructure.com`) is the provider of
  the address it hands on, else of the tool its id names. A tool's id is a whole number from 1: a module's tool with id 0
  (one Canvas could not match to an installed tool) joins no card by it. Where several places share a tool's id, the
  provider is taken in this order: a known launch address, a tab's label that names one, an item's title that names one,
  an unknown site, then a tab's own label — so an unknown publisher's tab named "Course Materials" is that publisher's
  card. An item on a site of its own takes a provider from its title only before a colon or a dash ("Knewton Alta: Unit
  2"; "Pearson Correlation" on stats.example.org stays that site's). An unknown provider is its whole host: two tools of
  one school are two cards, each named by its host ("jupyter.school.edu", "library.school.edu"), and one alone by its
  site; a host on a shared service (herokuapp.com, amazonaws.com, instructure.com) is never named by that service, and a
  US district is its own ("lausd.k12.ca.us"). The search hub's */open* lists such a tab once, as the tab. Each row carries `provider` (the table's id) and `icon` (a word of icons.js:
  book, video, disc…), both null for a provider the table does not know; the app draws its letter as before. The mock's
  CHEM course has Macmillan's week-titled assignments, a module item and a "Course Materials" tab of the same tool; the
  screenshot suite's `light-36c-external-tools-macmillan` is their one card's list.
  More from Canvas, the sidebar's last section (Shell/MoreFromCanvas.swift), as the web's sidebar has it: what the
  school added to Canvas's own navigation that no API lists — an account's tools with a place in it ("My Materials",
  a library, Studio), History, Help, sometimes a link — under the app's own places and the courses, in Canvas's order
  (native-app.js `moreFromCanvas`: the page's `#menu`, else the last one seen; kept with the sidebar's other answers
  for the next launch, read again with them). A school that adds nothing has no section; on Brightspace it is "More
  from Brightspace", its navbar's links less what the app draws (none on the mock). History is a clock, Help a
  question mark; a tool shows its own icon (the PNG the school's page drew of it, else Canvas's inline drawing — never
  fetched here from another site) on a small rounded tile of the school's nav colour, as its icons are drawn for
  Canvas's dark nav, else the external-link mark on that tile; a tool or a link has the external mark at the row's end,
  and its tooltip says what a press does. A row is not a place to be chosen: a press does what it says. History and
  Help open one popup over the window, never blocking it (FloatingPopup.swift; the other opened takes its place),
  listing the rows the web's sheets list, read afresh each time (`moreHistory`: Canvas's recently viewed, each with its
  glyph in its course's colour, its course, kind and when; `moreHelp`: the school's help links, and a foot when Report
  a Problem was left out, as it needs Canvas's form) — read before the popup comes up, so it is whole from its first
  frame, as the web's sheet and the Windows app's popup are (an error: the popup says it, with Try Again); pressed
  again while it is up, it comes to the front as it is. A row picked closes it and opens an address of the school's as
  any is (its screen here, else Canvas's page) and another site in the app's browser window; its menu has Open, Open in
  Canvas (the school's own) and Copy Link. A tool opens in the tool window on Canvas's launch page for it, borderless
  (light, as tool pages open, the app light or dark); a link opens its page in the app's browser window. The screenshot
  suite's `70` pictures: the section (`-SimplSidebarScroll more` scrolls the sidebar to it), History, Help, and My
  Materials in the tool window (in light and in dark: its page light).
  (1.3.68) Alta: a large picture or canvas (at least 140 × 100) counts as a graph too, and in dark mode each graph sits on
  a white card of its own (drawn in dark ink for a white page, its ground clear, it was lost on dark glass). A question
  is shown once Alta has finished drawing it — nothing changed on the page for 350 ms, no maths or answer box still
  loading, its pictures in — or after 2.5 seconds at most, a pending showing called off if the part goes again; until
  then the popup shows a skeleton of the question (`AltaSkeleton`: a glass panel for what it asks and one for its
  answer, their lines pulsing gently, still under Reduce Motion).
  (1.3.69) More than one window: File ▸ New Window (⌥⌘N; ⌘N stays New Task) opens another window of Simpl on the
  place the focused one shows, and a sidebar row's menu has Open in New Window. A window more (`MoreWindow`) keeps its
  own place, Back and Forward, search and sheets, but runs on the main window's engine (`Engine(sharing:)`: its calls go
  to the main window's page, and the sidebar, the counts and `dataVersion` follow it), so a change in one window shows
  in all; signing in stays in the main window. The menu bar acts on the focused window's engine. Grades: a weighted
  course's Assignment Groups card (the course's Grades and the Grades page's course detail) starts with the web's
  breakdown bar — a piece a group, as wide as its share of the grade, filled in its colour once something in it is
  graded, dashed until then; a piece pointed at names its group, share and score (`GradeWeightBar`; each group's
  `weight` and `graded` from native-app.js `courseGrades`, from the grade model's `weightBar`).
  (1.3.70) Alta: a graph in what a question asks, larger than 600 × 440 (1.3.98; 300 × 220 before), is drawn small (`data-simpl-mini`, a CSS zoom
  measured on its drawing, not its box); a click on it, or the Enlarge button the popup lays beside it, opens it large
  in a floating window of its own (`AltaGraphWindow`: a picture of it taken at its own size, so it is sharp), which goes
  when the question is answered (Alta's main button pressed), another part comes, the page goes out of sight, or the
  popup closes. In dark mode graphs are now turned over with the question by default (light lines on the dark glass);
  the sun button beside a graph, or Light in its window, gives Alta's own colours on a white card, remembered
  (`SimplAltaGraphsLight`). Loading is recognised: a page, or parts that are all a spinner, a skeleton, an
  indeterminate bar, something `aria-busy`, or words saying it is loading, stay out of sight (the popup's skeleton) for
  up to 20 seconds, and a question with something still loading in it waits up to 4 seconds before it shows; the
  overview waits the same way. The haze behind the quiz popups' floating buttons is a blurred capsule, with no edges.
  (1.3.71) 1.3.70 as a release proper (the betas 1.3.66–1.3.70 for every Mac): a beta's release is never edited into
  a release, so the same code goes out again under the next version.
  (1.3.72) Developer mode (`DevMode`, Shell/Developer.swift), hidden until asked for: seven quick clicks on the app's icon
  in Settings ▸ About, or Control-Option-Shift-Command-D anywhere (again to leave). Unlocked, the Simpl menu has a
  Developer menu (the Developer window, Save Alta / Macmillan Page Code — no longer in the popups — Refresh, Reload,
  Check for Updates Now, Copy Diagnostics, Leave Developer Mode) and the Developer window (no Window-menu item of its
  own): Overview (versions, where the app runs, live diagnostics), Updates (state, feed, beta / pause — (1.3.86) no
  automatic switch any more — the version put back from, a feed address of its own, Check and Install Now), Engine
  (its state, Refresh, Reload, Forget Kept Answers, and a console: any `BCVNative.call` with JSON arguments, its
  answer pretty-printed), Alta & Macmillan (state, Save Page Code, Error Logs — Alta's Error Logs button shows only in
  developer mode now — and the graphs' light colours), Interface (What's New, the tour, the setup, their resets, the
  look for this run, the main window's size) and Preferences (every one the app keeps, filtered, removed one by one;
  its folders). Critical Updates: the release workflow's `critical` switch publishes a release proper titled "Critical
  Update" with a mark in its notes; publish-site.sh names the newest one in both feeds (`criticalVersion`), and a Mac
  on an older version installs the feed's newest at its next check (Updater.swift) — paused, a version put back, a
  quiz open: nothing holds it back — with a notice over the window while it installs, tried again every ten minutes if
  it fails (1.3.102: every five). Only copies from 1.3.72 on know to do this. (1.3.102) A running copy looks for one
  every minute between its checks (1.3.109; every five minutes on an expensive network or in Low Power Mode, the feed asked only whether it changed) (Updates, below), so a Critical Update reaches a Mac in use within minutes.
  (1.3.74) The 3D builder's shapes are laid out by VSEPR, not found by forces (`Tools/MoleculeShape.swift`, `VSEPR`):
  round each atom its bonds, lone pairs and an odd electron take the directions their number gives (a line, a
  triangle, a tetrahedron, a trigonal bipyramid with lone pairs round its middle, an octahedron with lone pairs
  opposite each other), each lone pair squeezing the bonds a little (ammonia 107°, water 104.5°) and an odd electron
  letting them open (NO₂ 134°). The molecule grows out from its first atom, each atom's groups set round the bond it
  was reached by and turned about it to stand staggered, a bond across from a lone pair preferred (H₂O₂ skewed); a
  molecule being built, or one from the Lewis maker, is then turned to lie as its atoms were (Horn's best fit), and a
  ring is eased into shape from where it is drawn (bond lengths, each atom's angles, atoms kept apart). The forces
  pushed each atom's groups apart with nothing pushing back on the atom, so CO₂ settled bent at about 90°, water at
  120° and methane from the Lewis maker square — and the Lewis maker, which takes polarity from the 3D shape, called
  CO₂, BF₃, SF₆, XeF₄ and nitrate polar. Ozone's bond dipoles are nothing (one element) and its lone pairs' cancel, so
  it reads nonpolar now (its real dipole is its formal charges', which the model does not count).
  (1.3.75) A preset laid out afresh with more than one centre (a chain, a ring) is turned to face the viewer, its
  principal axes across, up and towards the viewer (`VSEPR.face`): ethanol lay along the line of sight, end on.
  (1.3.76) An element's page has a third view of its atom, Cloud (Atom · Orbitals · Cloud, `PTModel`): the electron
  cloud model, every electron as dots where it could be found, densest where it most likely is, with no paths
  (`Tools/ElectronCloud.swift`, line for line Windows' `ElectronCloud.cs`: the same dots). Each subshell is a
  hydrogen-like orbital (its radial nodes kept) whose exponent gives it the mean radius of Slater's orbital for Slater's
  Z_eff (summed in whole hundredths), its electrons in its real orbitals by Hund's rule; the dots come from a radial
  table per (n, l) (inverse CDF) and |Y|² by rejection, drawn by SplitMix64 seeded with the atomic number in a fixed
  order — 12 000 + 1 500 a proton, 40 000 at most, shared out by electron with at least 600 to a subshell (so a few
  heavy atoms have a little more: Rg 40 200, the most). Drawn to scale as SceneKit points (`AtomScene.cloud`,
  `CloudState`), one node a kind (s, p, d, f, grey): ink in light, light that adds up in dark; the nucleus a dot on a
  halo over them, not to scale; tipped so z stands up, turning once in 40 s and a few dots drawn afresh ten times a
  second, unless Reduce Motion is on or the screenshot suite is picturing it. Chips under it choose All or one subshell:
  that one coloured, the rest grey, the view framed by its 95% radius, its Z_eff and mean distance said (in pm, to one
  decimal only under 9.95, so never "10.0"), and how much larger it is drawn than in All. The tour's Orbitals step is
  still done by Orbitals alone. `-SimplPTModel cloud` and `-SimplPTSubshell 3d` (or `all`) set the view for the
  screenshot suite (`light-55k-ptable-cloud`, `dark-55l-ptable-cloud`; 1.3.80: in Atoms & Orbitals, `light-55k-atoms-cloud`,
  `dark-55l-atoms-cloud`); `-SimplCloudCheck YES` writes the model's test
  vectors to the console (`cloud-check …`). Moments that cut now move as the rest of the app does: why a reply, a
  comment, a hand-in, the setup or a grading scale was not saved fades in and out (what is under it moving on
  `Motion.snappy`), the Critical Update notice comes down from the top, Match's grid and the school picker crossfade,
  the tour's end and a set known or learned bounce a green mark once (`FinishMark`), and the radial colour picker
  shrinks back as it grew before its row closes.
  (1.3.77) External Tools: the cards stand in columns of their own heights (`Masonry`, a `Layout`: as many 320–480 pt
  columns as fit, each card measured at the column's width and put in the shortest column so far), so a short card no
  longer leaves a hole beside a tall one; a course's chip is offered at most its line's width (`WrapLayout`) and cuts a
  long name short; a tool known only by its site no longer shows it twice. A tool used in assignments or module items
  (`instances`, native-app.js `externalTools`, which now gives each course its `items` and each tool its `next`) is a
  card that is a button as a whole (it gives a little under a press, unless Reduce Motion is on): it says the next still
  to do and how many there are, and opens a popover beside it listing every one (`ToolPlacesList`: under each course's
  name when there are several, each with its line and status, the next tagged; a filter past eight; arrow keys, Return,
  Escape), the chosen one opened as a chip opens its course (Knewton Alta and Macmillan in their popups, the rest in the
  tool window). Its chips stay for the courses where it is also a tab. `-SimplExternalToolList knewton` opens the list of
  the first tool whose name has that word (the screenshot suite's `light-36b-external-tools-list` and
  `dark-36b-external-tools-list`).
- **Courses, a course and its sections, after the UI review (the courses findings; the same on Windows).** Courses:
  the filter reads Current · Past · Future and the line under the heading says what it shows (allCourses `subs`:
  "Fall 2026", "2 past courses", "None yet"); Choose Courses is named in the toolbar, on a symbol of its own. A card's
  banner names the course in white only where white stands 3:1 against its colour (else near-black); under it the
  course's other name only when it says something new (`altName`: a nickname's real name), else its term and teacher
  (`detail`); its five sections are named under their symbols (Grades, Work, Modules, Files, News); a course with no
  score says "–" and why in the ring's tooltip (`scoreWhy`). A section's count sits right after its heading
  (`PageSection`/`CardSection` `count:`) — Courses, Groups (no "1 current · 1 past" line any more), Files, Discussions,
  Assignments, People, Replies. Quizzes have their own symbol (`questionmark.square`); the checklist is To Do's. A
  course home shows the course's name under its title only when nothing on the page says it already. Modules: a course's
  only module opens by itself, and a locked one says when it opens under its name, closed. A module's file opens as the
  Files screen opens one (`openModuleFile`): by its id, through the engine's `fileInfo` — the file's own download
  address, with its one-time key — into Quick Look under the module's name for it; the module's own address (which needs
  the session and Canvas's redirect to its file host) only when Canvas will not say. (1.3.103) So does a Canvas file's
  link anywhere else — in an assignment's instructions, an announcement, a discussion, a page (`openLinkedFile`,
  through `openWeb`) — the Files page's own address for one too (…/files?preview=12, …/files/folder/Week 3?preview=12),
  the link a teacher copies from there — the address made from the link keeping its own key (`verifier`). Quick Look's
  panel finds its controller along the chain of responders of the key window, or the main window once the panel has the
  keys, and looks again whenever either changes: a hook to `FilePreview`'s controller sits in the chain of each window
  it may look in, the file is kept and handed back whenever it looks again, a web view that answers the panel itself (the
  instructions', after a link in them was clicked) lets go of the keys, and a moment after the panel comes up it is
  checked — the file in it, or the panel put away and the file opened in its app; never "No items selected". The
  screenshot suite clicks the instructions' file link (`-SimplWalk filelink`, light-14e) and fails on a panel left empty
  (`quicklook_check`, "[files] Quick Look: 1 item, …" on the console). A tool in a module, a tool's
  course chip and the home's More rows end in the window's symbol (a Simpl window); ↗ is only for the browser. People:
  a section of one (the teacher) runs across the card with a named Message button. Files: one vocabulary for a file's
  kind (`typeText`: PDF, Image, Spreadsheet, Text…). A discussion's post is drawn as plain text at once and the web
  view's own drawing fades in once it has measured (`RichText(placeholder:)`; the screenshot suite's console says
  "[richtext] not measured" if it never does); a Brightspace topic's forum is "in Weekly Discussions" on the
  discussion's symbol, never a person's initials. Groups: a card's name wraps to two lines, its course is named in full
  (`course`, the code in its tooltip). External Tools: rows and the card's next say "Unit 2" (`short`), the list opens
  under its card's foot (a down chevron there, up while open), no taller than the room left in the window, on a solid
  ground. The tool window's Dark Page moon shows only in Dark Mode, where it does something.

- **The tools after the UI review (1.3.78).** The Tools page is in sections by what a tool is for — *Study* (Flashcards,
  Focus timer, Citation generator, Grade needed; since the tools of their own, the Word Counter, the Study Planner and Verb Conjugations), *Math & science*
  (Calculator, Graphing calculator, Unit converter, since then Statistics and the Matrix Calculator; the Periodic table
  until 1.3.80 moved it to *Chemistry*), *Files*
  (File converter, Merge & split PDFs, PDF annotator, Image to text; (tools) Compress Files) and *Biology*, as on
  Windows — each tile's line one short line, three tiles across from about 900 points of page (`ToolGroup`, `ToolGroupSection`,
  `ToolTile.columns`). An open tool has one pin control, in its heading, named for what a press does (*Unpin* / *Pin to
  Toolbar*); the window's toolbar no longer adds a second. The pins past the toolbar's room go into a *Tools*
  pull-down (`ToolsCenter.toolbarSplit`, `MorePinsMenu`), so a screen's own buttons keep their room — the same in a
  tool window and on a popup's bar (`PopupPins`); 1.3.78 held three at most whatever the room, 1.3.79 measures it (see
  below). A tool picked in the pull-down opens its quick version in a popover under it.
  A pin's popover and its floating panel are headed by the tool's tile name with a › that opens the whole tool (the one
  way to; *Unpin* is in the pin's own menu, not a button beside the name), and the popover has *Pop Out*, which takes
  the quick panel out into its floating window (`ToolFloats.popOut`); the floating panel has a plain ✕ at its top left
  (Esc too) in place of the two window lights. Every running-timer label — the pin, the dial, the panel, the tile —
  reads one second hand (`FocusClock`, moved on the second the time left changes), so they never disagree; the timer's
  Pause/Start is in its phase's colour (`glassButton(prominent:tint:)`: a `.tint` put on a glass button from outside
  never showed; every tool's coloured button now takes its colour this way); the ring no longer names the phase the
  picker shows; the panel's foot reads "Session 1 of 4 · none finished today"; "Ends 7:17 PM".
  *Periodic table*: its heading names the page shown (Periodic table, Lewis structures, Molecule builder), with the
  pages in words as a segmented control on a row of their own under it, beside the search and Tour (`PTPageBar`, which
  replaced `PTHeaderControls`); the 3D builder's symbol is its own. On an element's page the heading's one back is
  "‹ Table" (⌘[) (`PTNav.elementOpen`, `closeElement`). The tour is offered the first time ("Take a quick tour?", Take
  the Tour / Not Now; `-SimplPTTourOffer YES` shows it for a picture), never started on its own; every card has Skip
  Tour, a ✕, Back and a Next that always works; Esc ends it (before it closes the tool's popup); ended
  early it counts as seen; the card sits beside the lit spot — under, over, or to its right (`PTTourOverlay.place`) —
  the first steps lighting the top rows and carbon rather than the whole table. Coloured by a property, a square too
  small for three lines shows the value in place of the number, and the trend gutter is slimmer. The legend and colour
  rows fade at a cut edge with a chevron that scrolls them on (`ChipScroller`). The element page's "Drag to turn ·
  scroll to zoom" sits on one line at the 3D view's foot; its comparison bars show the rank the words beside them give
  ("above 59%"), in the element's colour for that view. The Lewis and 3D builders, laid out narrow, carry Quick/Draw and
  the preset in the drawing's heading, the drawing about half the height (`PTNarrowReader`); the 3D builder's turning is
  a button named for what a press does (*Stop Turning* / *Turn*); a lone ion's charge sits at its symbol's top right,
  readable in the resonance thumbnails. The pin's small table shows the hovered element (or the first match) in its own
  card only, its squares larger (480-point popover). *Grade needed*: on a narrow window the answer comes first; the long note is under *How This Works*; the
  pieces and their groups use the engine's words ("Midterm 2 · 28.5% of your grade", "Midterms are 57% of the grade");
  the pin's fields are *Grade now %*, *Still to come % of grade*, *Goal %*; its sums round half up on the decimal value.
  *Citations*: the fields' hints say the form wanted ("First Last; First Last", "YYYY", "e.g. The Atlantic"), never a
  made-up value. *Unit converter*: the answer with its unit ("212 °F") and "= 100 °C" under it; Value, From, ⇅, To in
  one column. *PDF annotator*: "Select words on the page to highlight, underline or strike them." on the page until
  words are selected; *Merge & split*: "Click a page to leave it out. ⟳ turns it.", the turn button larger with a
  tooltip. *Flashcards*: studying, the bar reads "‹ Cell Biology  Learn" and the tool's line under its name makes way.
  For pictures, `-SimplTool <key> -SimplToolFile <path>` hands the tool opened a file as a drop on its pin does (a PDF:
  the annotator's page; Merge & split opens it in Split).

- **Pins, pulled-out panels and tools' pages (1.3.79).** *Every pin that fits*: the toolbar shows as many pins as it
  has room for and only the rest go into the *Tools* pull-down, from the end (a running timer last), measured as the
  toolbar is laid out and again as the window is resized, the sidebar shown or hidden or another screen comes up
  (`ToolbarFit`, an unseen `ToolbarFitProbe` in each pin and in the pull-down). In the main window (and a window more)
  the search field gives its room first: the pins are as many as leave it 140 points (`ToolbarFit.searchRoom`, its
  prompt whole) — the toolbar narrows the field as items are added, so its width says what is left; once the toolbar
  folds it into a button, or puts a pin or the pull-down into its » menu, a pin folds, one at a time, and that count is
  held as the most for that width until the window is a pin's room wider. A tool window (no search field) shows a pin
  more while nothing — no pin, not the Dark Page moon — has gone into the » menu. Back, Forward, Refresh and the title
  come before the pins and never make way for one. Until the pins have been measured, three at most, as in 1.3.78. A
  popup's bar shows every pin that fits beside the popup's name (240 points kept for it: `PopupPins`, a `ViewThatFits`
  of all the pins, then one fewer and the pull-down, and so on — never fewer than 1.3.78 showed there). *A pulled-out
  panel moves*: by its ground, its head (the
  tool's tile, the room beside the name, and the name itself — a press that does not move opened the whole tool until
  1.3.83) and its grip (`PanelMover`: AppKit's "movable by its background" never moved it, every press landing on the
  SwiftUI hosting view), kept on the screen (its head at least), and it comes out again where it was last put
  (`SimplToolFloatPlaces`, a top-left corner per tool; a grip drag that pulls a panel out puts it there too). *Tools'
  pages light*: an external tool's page is shown light whatever the app's appearance — the tool window's web view
  (its appearance light, so the page's colour scheme, its fields and the ground under it are light: `ToolDarkPage.look`),
  Macmillan's, and Alta's page with its glass panels, graphs and buttons (`AltaQuiz`: the page's region in a light
  environment over a light ground; the page is told light, so its question is never turned over; the graph window on
  white). The tool window's Dark Page moon (Dark Mode only) is off until turned on, and turned on it draws the page
  dark as before; Canvas's own viewer in the feedback popup keeps Dark Page on by default. The popups' bars and rails
  and the tool window's toolbar keep the app's appearance.

- **Open Full Screen in a pin's panel (1.3.83).** The owner, of the Graphing calculator's pinned panel: its top-right
  button "shouldnt be pop out, it should be full screen". A pin's popover, and its pulled-out floating panel, now have
  *Open Full Screen* at their head's right end (`QuickFullScreenButton`: `arrow.up.left.and.arrow.down.right`, its
  tooltip and VoiceOver label "Open Full Screen") where the popover had *Pop Out*: the panel closes (a floating one is put
  back) and the tool opens whole, as its tile on the Tools page opens it — its popup spanning the window over the
  sidebar, or at the size last given it (`ToolsCenter.openFullScreen`, `popUp`) — with what was begun in the panel
  carried over (`QuickCarry`, said by each quick version as it changes with `quickCarry(_:)` and read by the panel):
  Grade needed's three numbers, the element searched for in the Periodic Table's or typed in Atoms & Orbitals', the
  link pasted to cite, the unit converter's kind, units and value (`UnitConverterTool.takeHandOff`), a projectile's
  speed and angle from Kinematics & Projectiles' pin (`MotionCompact`), two vectors from Vectors' (`VectorsCompact`),
  the Circuit Builder's Ohm's law and list (`CircuitCompact`), the calculator's
  line, sums, memory and Rad/Deg (`CalcEngine.handOff`), a codon looked up, a cross, a Hardy–Weinberg value, the lab
  calculator shown, (tools) the Equation Balancer's equation, a formula for its molar mass, the gas law's four fields, a pH (or pOH, [H⁺], [OH⁻]) typed, the Word Counter's text, Statistics' line of data (to its Describe page), the Matrix Calculator's A;
  the graph's whole drawing — expressions, sliders, tables, notes and view — by Desmos's own
  `Calc.getState`/`Calc.setState` from the pin's web view to the whole one's (`GraphWeb.carryOver`; the whole one's graph
  before it is a ⌘Z away). The focus timer, Flashcards' sets and the lab's numbers are the whole tool's already. A panel
  as it opens carries nothing, and the tool opens as from its tile; with something carried, the tool's popup if it is
  open already is made afresh to take it (`popUp(afresh:)`, `ToolsCenter.popupMade`) — as it is too when a quick
  version hands its tool something itself (Return, a set, a dropped file). The panel's name is its title alone: its ›,
  which opened the whole tool as well, is gone, so Open Full Screen is the one way from the panel to the whole tool
  (tools-22); on a floating panel the name still moves the panel. Pulling a panel out stays: by the grip along the
  popover's foot, and by *Pop Out* in the pin's menu. Pictures: `light-54i-pin-graph` (the graph's panel), and
  `light-54j-pin-full-need` and `light-54k-pin-full-graph`, a pin's Open Full Screen pressed (`-SimplPinFull <key>`; the
  panel's numbers `-SimplPinNeed 85,30,90`, a graph drawn in the pin's Desmos `-SimplPinGraph "y=x^2"`; the console says
  what was carried: "pin full screen: need (carried: goal, now, worth)", "graph: the pin's graph put in the whole one").

- **Fast and fluid (1.3.78).** A screen shows its whole content at once whenever this session has already read what
  it shows, and the window moves the same way however it is moved:
  - *Held answers* (`AnswerMemo`, Engine/ReadAhead.swift): every read's answer is held in memory, decoded, by its call
    and arguments (the arguments that only set a preference — To Do's grouping, Hide Completed, the calendar's view —
    left out of the name it is held by, and such a read is always asked). A screen opens on it at once (`Loader`,
    `Engine.recent`) and asks again only once it is stale: after a minute, or once any call that is not a read has
    succeeded (a tick, a hand-in, a post, a setting: `Engine.fetch` makes everything held stale; `changed()` does too,
    unless an action did a moment ago). A stale answer is still shown at once and replaced quietly, what changed moving
    into place. The same read asked twice while it is on its way is asked once. Answers are decoded off the main thread,
    and the answers kept on disk (AnswerCache, sealed by the vault) are read and opened off it too. Signing out lets go
    of all of it.
  - *Reading ahead* (`ReadAhead`): once the window has been quiet a moment after it opens, the Dashboard (when it is
    not the first screen), To Do, the Calendar (its view and the span round today), Grades, Notifications, the Inbox,
    All Courses and every course's home are read, two at a time at most, never while a screen is waiting for a read of
    its own, a breath between them. A sidebar row, a course's card, a section's pill or a row of work the pointer rests
    on for 80 ms has what it opens read ahead, first in line (on a Mac every press comes after the pointer arrives, so
    this is also what a press would ask). Nothing that marks something read (a discussion, a conversation) is read
    ahead. These reads are quiet: the screenshot suite's pictures never wait for them.
  - *The Dashboard kept* (`DetailStack`): opened once, it stays — over the stack, out of sight and out of reach of the
    pointer while another screen shows — so its cards, their pictures and where it was scrolled are as they were when
    the window comes back to it. It reads again only when it is shown and something was done meanwhile, each card
    changing in place; (1.3.80) shown on what is older than a minute, it is checked live at once (Live checks, below).
    Its title stays in the stack (`DashboardChrome`; its view and New Task are
    in its page, by the greeting). It and its
    parts act through the engine held as a plain value of the environment (`EngineLink`) rather than observe it, so,
    put away, the Dashboard is not drawn again each time the engine changes (a search typed, a count read).
  - *Waiting*: a screen's spinner shows only after a third of a second, so a quick answer replaces an empty page rather
    than a spinner that flashes.
  - *A move draws little*: where the window is and how it last moved (`Whereabouts`, Engine/Navigator.swift) are
    observed apart from the engine, so a move draws again only what shows it — the sidebar, the toolbar's Back and
    Forward, the stack, the preview over the window, the Go menu — and not every screen and row that reads the engine
    (the Dashboard kept under the stack among them).
  - *Motion* (Shell/PageMotion.swift, one language with the web and Windows): going deeper — a link, a row, a card,
    Forward, a course's home to a section — the new page comes in from the trailing edge 32 points (the web's) as it
    fades in, and a place left drifts 12 points the other way as it fades out; Back is the same in reverse; another
    place picked in the sidebar cross-fades. All on `Motion.page` (critically damped, response 0.34):
    a move made mid-way starts from where things are and nothing waits in line. A page that arrives waits until its
    content is there (a third of a second at most), so it never slides in empty and then fills. A move within a place
    swaps the stack's screen at once (the screen under it kept as it was) and the new top arrives by itself; one to
    another place fades the old page out the way the move goes (`PageExit`) while the new one arrives (`PageArrival`),
    unless the place comes with a screen already on it (an assignment opened from Grades, To Do or the Dashboard; Back
    or Forward to one): then, as with a push, the old page goes at once and the screen on top arrives, the place's
    own page never shown under it (`RootUnseen`). A place's stack is made with nothing on it and given its screens on
    the run loop's next turn, once it is up: a `NavigationStack` made with screens already on it set them away itself
    as it appeared (the console said `stack: … set its own path, 1 screens to 0`) and showed its place's list.
    Under Reduce Motion every move is a cross-fade. No element is morphed from one screen into the next: the card and
    the course's heading are drawn by different screens, and a morph between them could not be kept clean.

- **The engine's side of live checks (Simpl Courses 2.99.35).** A screen left open is checked against Canvas by asking
  for its calls again with `maxAge` (seconds): every Canvas read the answer is made from that is older than that is read
  again, and nothing younger is (`native-app.js` hands the age to each call, which passes it to every store read it
  makes, so two calls under way at once keep their own ages). Who is signed in, the course colours and the school's
  help links are not read again for it. With `known` (the `hash` of the answer on screen) a call answers
  `{ unchanged: true, hash }` when nothing changed — nothing to decode or draw — else `{ unchanged: false, hash, value }`;
  the same data always makes the same answer. `BCVNative.stats().reads` counts the Canvas reads the page has made, for a
  check's log line. What a course holds (its record, tabs, sections, people, groups, folders; modules, pages, files,
  quizzes, the front page and the syllabus already) is kept 10 minutes at most. Against the mock school only,
  `liveDevChange` (`done`, `grade`, `task`) changes the school behind the engine's back, for CI to see a check find it
  (its grade change moves a total half a point, inside its letter's band). A check (an age) reads an open conversation
  with `auto_mark_as_read=false` and marks a conversation or a discussion read only when the app passes `seen` (the
  student is at the window); without it a check writes nothing to Canvas, and opened, a conversation or a topic is
  marked read as before. The concluded courses All Courses lists are read at ten minutes' age at least.

- **Live checks (1.3.80).** A screen left open no longer shows what it was opened on until something is done: the
  screen on show is checked against Canvas while the window is in front of the student, and only what changed is put
  in place (Engine/LiveRefresh.swift; the plan, the fingerprint and the comparison in Engine/LivePlan.swift, checked
  by scripts/dev/swift-checks).
  - *When*: every minute; a course's or a group's pages (its home, modules, pages, files, assignments, discussions,
    announcements, people, quizzes, syllabus, its grades, an assignment, a discussion) every ten minutes, as the engine
    keeps their data ten minutes at most. Each screen says it is on show with `.liveChecks(…)` (a `Loader`'s screen
    with `.liveChecks(model)`); `DetailStack` says which screen is on top (`\.liveOnShow`), the Dashboard kept over the
    stack says so itself (`shown: active`). Two side by side (the Inbox's list and the conversation beside it) are each
    checked. One check at a time in the whole app (the windows share one page); a check due while one is under way is
    skipped; a check with no answer in two minutes is let go. After a check that did not work the next waits 2, 4, then
    5 minutes (five times the interval at most), and one that works sets it back. A screen shown again — Back to it,
    the Dashboard come back to — on what was read longer ago than its interval is checked at once; so is a window
    brought back after longer than that.
  - *Held back* (`LiveHolds`), and run as soon as it ends: the window minimized, covered or not in front, the app not
    active, (1.3.85) nobody at the Mac (`away`: the screen locked — `com.apple.screenIsLocked` / `…Unlocked` — the
    displays or the Mac asleep, another user's session in front), no network (`NWPathMonitor`), the page loading or a
    screen's own read under way; a quiz, an Alta or a Macmillan assignment open; the student typing (a field of the
    screen's being edited — the toolbar's search field only while keys are pressed in it, as it keeps them after a
    search — or a key in the last 5 seconds); What-If on; any sheet, dialog, popover, popup (Hand In and Send from Phone
    among them; a tool's own popup aside, so a screen beside a tool is still checked, and (1.3.85) History's and Help's,
    only there to be read, as in the Windows app), menu or the tour; the mouse held down (a drag); a counter's panel or a
    row's preview open.
    Something that comes up while a check is asked also keeps what it found from being put in place: it is checked
    again as soon as that ends.
  - *Marks nothing read unless the student is there* (1.3.85): each read a check asks carries `seen` only while the app
    is in front with one of its windows key or main and in sight, the screen unlocked and the Mac awake
    (`LiveHolds.seen`, asked in `Engine.liveAnswer`, as the Windows app's `Engine.LiveSeen`). Only then may a check of an
    open conversation or discussion mark what came in it read on Canvas; a message that comes while nobody is looking
    stays unread there and on the Inbox's badge until the student is back at the window.
  - *Only what changed*: a check asks each of the screen's reads again with the check's age as `maxAge` (60, or 600 for
    a course's pages) and the fingerprint of the answer on screen as `known` (`Engine.liveAnswer`). The fingerprint of
    every answer a screen reads is worked out as it arrives (`LiveFingerprint`, the page's own cyrb53 of the same JSON,
    held with the answer in `AnswerMemo`), so even a screen's first check is answered "unchanged" with nothing to send,
    decode or draw; a screen showing an answer kept from before compares the new one written out as JSON
    (`LiveCompare`). Nothing new: nothing is touched, so nothing is drawn — no flicker, no scroll, no focus moved.
    Something new: it is held and kept as a screen's read is, and the screen puts in place only the parts that changed,
    in one transaction on the house's gentle spring (a short fade under Reduce Motion), its rows and cards drawn by
    their ids so the rest stay as they are. The Dashboard checks each part apart — the day and its counters, Overdue's
    and Graded's numbers, the cards and how much of each is handed in (in Cards), Coming up (List) or the activity
    (Activity), the skyline — and only for the view on show: a counter's number rolls to its new count
    (`.numericText`; simply the new number under Reduce Motion), a row a check adds or takes away fades in or out where
    it is. Put away, the kept Dashboard is not checked. The sidebar's counts (`snapshot`) are checked beside every
    screen. A check never sets a preference (To Do's grouping, Hide Completed are left out of its arguments) and never
    checks a course's grades while scores are tried.
  - *Said on the console*, one line a check, never a name or a title: `live: dashboard unchanged · 12 reads · 0
    redraws`, `live: dashboard updated counters,day,list · 9 reads · 1 redraw`, `live: todo updated todo · 3 reads`,
    `live: assignments unchanged · 2 reads` — the parts that changed, the reads it made of the school
    (`BCVNative.stats`, page-wide) and, for the Dashboard, how many times it drew the page (`LiveTrace`). A check that did
    not work: `live: <screen> not checked (<why>) · next in 120 s`; one found while something came up: `live: <screen>
    found <parts>, held (<why>)`; in the screenshot suite and developer mode also `live: <screen> waits (<why>)` as a
    hold begins.
  - *For the screenshot suite*: `-SimplLiveEvery 5` checks every 5 seconds (a course's pages every 50, or
    `-SimplLiveCourseEvery`), `-SimplLiveChange done:6` (`grade`, `task`) changes the mock school behind the engine's
    back that many seconds after the first screen is shown (`live: dev change done made (assignment:1009)`), and
    `-SimplLiveShotAfter 5` holds the picture until that many checks are done.

- **Popups that never block the window (1.3.79).** Every popup but a quiz being taken stands over the window without
  getting in its way (`Shell/FloatingPopup.swift`): each is a window of its own over the main window (`PopupPanel`, a
  child panel — above it, moving with it, key when clicked, never the main window), put up and closed by the window's
  `FloatingPopups` (`Engine.popups`, by key). Nothing round one is dimmed or caught: the sidebar, the toolbar, the
  search and the screen under it are used as ever, a soft shadow under it; going to another screen leaves it where it
  is until it is closed — its ×, Escape while it has the keys, ⌘W, or its own Close, Cancel or Done (what it shows
  closes it through `@Environment(\.popup)`, called as SwiftUI's `dismiss` was; `popupCloseHeld` keeps Escape, ⌘W and
  × from closing a hand-in or a message on its way, or a message with something written, whose Cancel asks first).
  Dragged by its head (`popupHeader()`; a strip along the top edge of every popup too) it moves, past the window if
  wanted. It comes in a little smaller and faded on a short ease-out (0.22 s; a fade alone under Reduce Motion) and goes
  the same way. Three sizes (`PopupLayout`): *large* — (1.3.80) the window's whole content, under the toolbar and over
  the sidebar (out or not), less 20 points all round (`FloatingPopups.largeRoom`), at most 1480 × 1040 unless the
  student makes it larger, sized again as the window is (`\.popupSize` tells what it shows), a large one over another
  a step down and along (and that much smaller, so it ends inside the window) — dragged by its head it moves, and its
  edges and corners resize it (a resize whose button comes up unseen ends at the next placing; `PopupEdges`: a band 5
  points wide inside each edge, 16 along each side of a corner, the frame-resize cursor over it; at least 480 × 340,
  at most the room and the screen), what it shows laid out at the new size as it goes; the size picked is kept in the
  app's settings by size key (`PopupSizes`, `SimplPopupSizes`: a tool's own, `tool.<key>`; else the popup's key up to
  a colon — `alta`, `macmillan`, `feedback`, `rubric`), a side left as large as the room would have it kept as filling
  it, and used the next time it opens — and as another tool is switched in — within the window's room, so a smaller
  window shrinks it and a larger one gives the size back; a popup moved aside keeps how far it was moved. 1.3.79 kept
  it clear of the sidebar while the sidebar was out (a tool had 740 points of a window 970 wide); *fitted* — as large
  as what it shows asks, as a sheet was, centred across the screen's side a little above its middle, its top and
  middle kept as it grows; *at* a point (a counter's list over its tile in 1.3.79; unused since 1.3.80, when the list
  grows out of its tile in the page again). A `.floatingPopup(key, title:, item:/isPresented:)` modifier puts one up
  as `.sheet` did. What each popup is now: a **tool opened whole** — from its tile on the Tools page, its pin's popover
  or floating panel (1.3.83: its Open Full Screen, or what it hands on), the pin's Open, a hand-off from another tool (`BioTools.open`) or
  `-SimplTool` — a large popup over the window (`ToolsCenter.popUp`, `ToolPopup`; one at a time, another switched in
  place with a cross-fade, opening at its top; its heading has its tile, name, pages, Pin and × and drags it; the Tools
  page stays behind it — no more "‹ Tools"); **Knewton Alta** and **Macmillan** assignments, the **feedback** on a
  piece of work and the **rubric ring**: large popups; **What's New**, **Hand In** (with Send from Phone and the file
  preview inside it, the popup widening for the preview), **New Task**, **New Message** (File ▸ New Message, or a
  person's Message in a course), the **grading scale**, a **what-if** assignment, a course's **nickname**, a counter's
  **card photo**, the Cell Explorer's **POPC** and Crosses' **Why these gametes?**: fitted popups (1.3.80: a
  **counter's list** is no popup — it grows out of its tile over the page and folds back into it, as in 1.3.77); the
  **quick look** at a piece of work: still the card grown out of its row in the window, but a press round it no longer
  closes it, going to another screen leaves it where it is, and dragged it moves (its ✕ and Escape close it) — a row in
  a popup has its card in a popup of its own over it, just under the row (or over
  it). A popup's SwiftUI view sits in a plain view of the popup's own, never as its window's content view (which sized
  the window to what it showed, and a tool asking for more room the more it had grew it until AppKit stopped the app),
  and a large popup is placed again over its first seconds, as the window settles (until it is dragged or resized); a
  fitted popup is centred clear of the sidebar while it is out (the window says whether it is:
  `FloatingPopups.sidebarMoved`, as a folded sidebar's view is still found where it stood; until the sidebar's view says
  where it ends it is taken to end where it last did, at first its usual width). A fitted
  popup is measured as what it shows appears and as it changes (a measure handed up as a SwiftUI preference never
  reached a popup whose view is not its window's content view: Hand In, New Task, New Message and Why these gametes?
  stayed at the size they were guessed at, cut off at their sides); one that draws its own glass — a work's card —
  measures itself and sizes its popup (`PopupHandle.fit`). Unchanged: the pinned tools' popovers and their pulled-out floating panels, popovers and menus, Quick Look's
  own panel and the Alta graph's window (all of which never blocked the window), the external tools' windows, and
  Alta's own pop-ups (a sheet on its popup alone). Still blocking, by design: a quiz being taken (a sheet: its timer and
  its integrity), the first run's setup, the sign-in, the tour, the Mac's confirmations and file choosers. An update
  never installs itself while a popup is up (`Updater.quiet`). The pictures: `light-35p…`/`dark-35p…` a tool's popup
  (the Periodic Table, Cell Explorer, DNA & RNA (`35s`), Flashcards) over the Tools page at 1024 and at the narrow
  size, and `light-35t-popup-dna-kept-size` DNA & RNA reopened at a size it was left at (`-SimplPopupSize
  tool.dna=760x520`: a launch's own sizes, `key=WxH` joined by commas, never kept). In a picture's run (or with the
  Developer menu unlocked) the console says where each large popup went (`[Simpl] popup tool: 984 × 668, 20 in from
  the window (sidebar out)`) and each size kept (`resized to … kept for tool.dna`).
- **Windows that size themselves (1.3.82).** SwiftUI sizes a window to the SwiftUI view in it — a popover's, or any
  window whose content view is a hosting view — each time it lays the window out; a window kept at another size (by
  the screen, or by what it shows asking more the more it is given) is sized back, pass after pass, until AppKit stops
  the app ("more Update Constraints in Window passes than there are views"). Mac 1.3.81 stopped so under External
  Tools' list: the list's room was reckoned each time its card was drawn, from the key window — the list's own once it
  had the keys — so a card drawn again under its open list (the tools' answer coming in a second time) asked 460
  points of a screen with some 330 over and under the card. Now the room is reckoned once as the list opens, from the
  card's own window and screen (`ToolCard.reckonRoom`: under the card as far as the window and the screen go, else
  over its foot, 460 at most), the list's measured height is taken up a turn of the run loop later and only past half
  a point, and the list may be shorter than it asks (`minHeight`), so a popover AppKit keeps shorter on the screen is
  left so. The Calendars, quiz instructions and Alta error-log popovers may be shorter too. A pulled-out quick panel
  (`ToolFloatPanel`) holds its SwiftUI view in a plain view of its own, as a popup does, and is sized by what it shows
  measuring itself, on the run loop's next turn (`ToolFloatRoot`, `ToolFloatPanel.fit`). The picture
  `light-36d-external-tools-list-reread` (`-SimplExternalToolReread YES`) reads the tools again under Knewton Alta's
  open list; `tool_list_check` writes `tool-list-check.txt` from the consoles (`[Simpl] tool list room: …`, `read
  again; … still open`).
- **The Chemistry section (1.3.80).** The Periodic Table is split into four tools under a *Chemistry* heading on the
  Tools page, just before Biology and built as its section is (`Tools/Chemistry/ChemTools.swift`: `ChemTools.kinds`,
  `info` — names, lines, symbols and a palette of their own — and `ChemToolsSection`, which `-SimplToolsScroll
  chemistry` scrolls to): **Periodic Table** (`ptable`, the key a pin of it always had: the table, its colours and
  trends, the element under the pointer, an element's facts page and the tour), **Lewis Structures** (`lewis`: the
  Lewis maker, `LewisStructuresTool`), **Molecule Builder** (`molecule`: the 3D builder, `MoleculeBuilderTool`) and
  **Atoms & Orbitals** (`atoms`, `Tools/Chemistry/AtomsOrbitals.swift`: the 3D atom, orbitals and electron cloud that
  were on each element's page, for any element — typed by symbol, name or number, stepped with ‹ › or ↑ ↓ in the field —
  laid out as the builders are, `PTWorkLayout`). *Math & science* keeps the calculators and the unit converter. Each
  tool has its own tile, pin (its quick version: the table small; Atoms & Orbitals an element to open; the builders
  their tile and Open), popup, heading — the Periodic Table's without its pages, its search and Tour left of its pin
  (`PTHeaderBar`; 1.3.87: on the heading's own row where the window has room for them with the search open, else on a
  row under it) — and command in the search field (`/ptable`, `/lewis`, `/molecule`, `/atom`; words such as
  "periodic", "orbitals" or "vsepr" bring them up among the suggestions; `PaletteAction.tool`). They hand over in the
  same popup rather than being one tool's pages: an element's facts page has its shells drawn flat (`PTBohrSketch`) and
  *Open in Atoms & Orbitals* where its 3D view was, and Atoms & Orbitals has *Open in Periodic Table* back to the facts;
  the Lewis maker's *Open in Molecule Builder* hands its molecule over (`ChemTools.openInMoleculeBuilder`), as the
  Biomolecule Builder's does. The table's tour is the table's alone: hover, colour, open an element, search, and last
  *Open in Atoms & Orbitals* (`PTTourEvent.atoms`). What each keeps (`ChemPrefs`: UserDefaults
  `SimplChem.<key>.<field>`, a picture's in a suite of its own): the table's colouring and the last element opened;
  the Lewis maker's molecule (`lewis.draft`, `ChemLewisDraft`); the builder's molecule and view (`molecule.saved`,
  `ChemMoleculeDraft`; `.style`, `.lonePairs`, `.labels`); Atoms & Orbitals' element and view, opening on the table's
  last element when it has none of its own. Before, nothing was kept but the pins (whose `ptable` key is unchanged) and
  the tour's seen flag, so nothing is carried over. With Reduce Motion the atom's electrons and nucleus, the orbitals
  and the builder start still. Launch flags: `-SimplTool ptable|lewis|molecule|atoms`; the old `-SimplTool ptable
  -SimplPTPage lewis|molecules` and `-SimplPTModel …` open the tools they are now (`ChemLaunch`, checked by the Models
  job); Atoms & Orbitals reads `-SimplPTElement`, `-SimplPTModel` and `-SimplPTSubshell`, the builder
  `-SimplPTMolecule`, when the launch opened them; `-SimplPTHandOff atoms` presses the facts page's *Open in Atoms &
  Orbitals*. The Developer window's Interface page opens each tool, resets the table's tour and forgets what they keep.
  Pictures (`chem_tools`, both sets): `light-35c-tools-chemistry`, `dark-35c-tools-chemistry`, `light-55o-atoms`,
  `light-55p-atoms-narrow`, `light-55q-atoms-from-facts`, `dark-55r-lewis`, `light-54h-pin-atoms`,
  `light-30c-palette-chemistry`; and the chemistry pictures before them, pointed at the tools (`light-55c-lewis`,
  `light-55d-molecule`, `light-55k-atoms-cloud`, `dark-55l-atoms-cloud`).
- **The Physics section (tools).** A *Physics* heading of its own on the Tools page, after Chemistry and before
  Biology, built as the Chemistry section is (`Tools/Physics/PhysTools.swift`: `PhysTools.kinds`, `info` — names,
  lines, symbols and a family of blues — `PhysToolsSection`, which `-SimplToolsScroll physics` scrolls to, `PhysQuick`
  for the pins, and `PhysPrefs`: UserDefaults `SimplPhys.<key>.<field>`, a picture's in a suite of its own). Each
  tool's arithmetic is Foundation only, in `Tools/Physics/Model`, where the Models job checks it against the
  specification's test vectors (`scripts/dev/swift-checks/main.swift`, "the physics tools"); every number is written
  to four significant figures, rounded half up, without float noise (`PhysMath.text`). The Developer window's
  Interface page opens each tool and forgets what they keep.
  **Kinematics & Projectiles** (`motion`, `Tools/Physics/Kinematics.swift`; `/motion` in the search field, or
  "kinematics", "projectile", "velocity", "free fall"): two pages. *1D motion* — the five quantities of motion in a
  line (Δx, v₀, v, a, t) in a grid; any three filled in, the other two found as you type with the two equations that
  found them (`Kinematics.solve`, Model/PhysMotion.swift: a velocity from v² = v₀² + 2aΔx takes the root whose time is
  not negative), a *g* button (a = −9.81 m/s²), Clear, Copy, and a position–time and a velocity–time graph beside
  (`MotionGraph`, a Canvas); fewer or more than three, or three no motion fits, say so inline. *Projectile* — speed,
  angle, launch height and g to the time of flight, range, greatest height (and when), impact speed and angle
  (`Projectile`), the path drawn to fit its card with the apex and landing marked and a dot flown along it in 1.5 s
  each time the launch changes (`TrajectoryView`; still at the landing under Reduce Motion), and *Hit a target*: the
  low and high angles that land it a distance off at this speed, or how far it can reach. SI in the fields; a
  *ft · mph* toggle shows the answers in feet and mph beside the metres. The pin: speed and angle to range and
  greatest height (`MotionCompact`), Return or Open Full Screen opening the tool on them (`tab`, `speed`, `angle`,
  `height` handed over). Launch flags: `-SimplTool motion -SimplMotion 1d|projectile -SimplMotionValues
  "v0=20,angle=45,h=0"` (also `x`, `v`, `a`, `t`, `g`, `target`, `units=imperial`), read only when the launch opened
  it. Pictures (`phys_tools`): `light-35d-tools-physics`, `dark-35d-tools-physics`, `light-77-motion-1d`,
  `light-77b-motion-projectile`, `dark-77b-motion-projectile`, `light-54s-pin-motion`.
  **Vectors** (`vectors`, `Tools/Physics/VectorsTool.swift`; `/vectors`, or "vector", "dot product", "resultant"):
  up to six vectors, lettered A–F in a colour each, typed as components (x, y, and z in 3D) or as a magnitude and an
  angle from +x (2D), 2D or 3D, degrees or radians (`Vec3`, `VectorEntry`, Model/PhysVectors.swift). Beside them:
  each's length and direction (in 3D the angle from +x and the elevation), the resultant with its length and angle,
  and for a pair chosen by letter the dot product, the angle between, the cross product (in 2D its z component), the
  unit vectors and the projection of one onto the other; Copy. The drawing (`VectorDiagram`, a Canvas): in 2D on a
  grid, from the origin or *Tip to tail*, the resultant dashed; in 3D an isometric projection, each vector with its
  shadow and drop to the xy plane. The pin: two 2D vectors to their sum (`VectorsCompact`), Return or Open Full Screen
  opening the tool on them (`vectors` handed over as "3,4;1,2"). Launch flags: `-SimplTool vectors -SimplVectors
  "3,4;1,2"` (components by commas, vectors by semicolons) `-SimplVectorsMode 2d|3d`. Pictures: `light-77c-vectors`,
  `dark-77c-vectors`, `light-54t-pin-vectors`.
  **Circuit Builder** (`circuit`, `Tools/Physics/CircuitTool.swift`; `/circuit`, or "ohm", "resistor", "series",
  "parallel"): a grid canvas (24 points a cell, `CircuitGeometry`) laid out as the builders are (`PTWorkLayout`): the
  palette at the left — battery, resistor, wire, switch, lamp, ammeter, voltmeter, ground — each dragged onto the grid
  (`draggable`/`dropDestination`) or added with a click; a part dragged to move it, clicked to choose it (a switch is
  thrown too), turned with R and removed with ⌫ (the canvas is focusable), and wired by dragging from one terminal to
  another; parts set end to end join as well. The circuit is solved as it changes by modified nodal analysis
  (`CircuitSolver`, Model/PhysCircuit.swift: nodes by union-find over links, wires, closed switches and grounds; a
  conductance for each resistor, lamp and ammeter (10⁻⁶ Ω); a current unknown for each battery; each circuit with a
  battery solved on its own, its ground or its first battery's − at 0 V; a wire's current told from its one
  neighbour in a series path). Every part's V, I and P are listed at the right (a row chooses the part; the part
  chosen has its value, Rotate, Delete and for a switch Closed), arrows run along each part with the current
  (`TimelineView`; still under Reduce Motion), a lamp glows with its power, and a shorted battery, batteries face to
  face or parts off on their own are said in plain words. Quick-build chips (*Series: 9 V + 100 Ω + 200 Ω*, *Parallel:
  9 V, 100 Ω ∥ 200 Ω*, *Voltage divider*: 12 V over 1 kΩ + 2 kΩ with a voltmeter reading 8 V), Clear, Save… and Open…
  as JSON (`CircuitDoc`), Export PNG… (`ImageRenderer` of `CircuitPicture`, black on white). Under the palette a
  calculator strip: Ohm's law from two of V, I and R (`OhmStrip`) and a list of resistances in series and in parallel
  (`SeriesParallelStrip`, k and M read). The pin is the strip (`CircuitCompact`), Return or Open Full Screen opening the
  tool with it (`ohmV`, `ohmI`, `ohmR`, `list`). The circuit and the strip are kept (`circuit.doc` as JSON). Launch
  flag: `-SimplTool circuit -SimplCircuit series|parallel|divider`. Pictures: `light-78-circuit-series`,
  `dark-78-circuit-series`, `light-78b-circuit-divider`, `light-54u-pin-circuit`.

- **The chemistry calculators (tools).** After the Periodic Table's four in the Chemistry section (appended to
  `ChemTools.kinds`, their names, lines, symbols and colours in `ChemTools.info`), each a tile, a pin with a real quick
  version, a popup, a `/command` in the search field and launch flags for its pictures; what each keeps is in
  `ChemPrefs` (`SimplChem.<key>.<field>`), and the arithmetic of each needs no window — it is in
  `Tools/Chemistry/Model/` (Foundation only) and the Models job checks the specification's test vectors
  (`scripts/dev/swift-checks/main.swift`, "the chemistry calculators"). Shared by them all
  (`Model/ChemFormula.swift`): `ChemFormula.parse` reads a formula — parentheses, hydrates (`CuSO4·5H2O`), charges
  (`Fe^3+`, `Fe3+`, `SO4^2-`, `MnO4-`, superscripts), subscript digits, a phase label `(s) (l) (g) (aq)` passed over — into
  its elements and counts, and types it with subscripts (`pretty`); `ChemElements` has each element's mass from
  `PeriodicData.json`; `ChemMolar.breakdown` a molar mass with its percent composition; `ChemNumber` writes a number
  to a few significant figures, rounded half up with no float noise (`sig`, `fixed`, `pretty` for "3.343 × 10²³");
  `ChemLaunchValues` reads the flags' `key=value` lists.
  - **Equation Balancer** (`balance`, `Tools/Chemistry/EquationBalancer.swift`; `/balance`, words *equation*,
    *reaction*, *coefficients*): one field for the whole equation (`->`, `→`, `=` or `⟶`; coefficients typed are passed
    over), balanced as it is typed (150 ms after the last key, or on Return) by `ChemBalancer` (`Model/ChemBalance.swift`):
    the matrix of each element's — and the charge's — count in each species, its null space found with exact
    fractions (`ChemFraction`, Int64 with every step checked for overflow), the one solution scaled to the smallest
    whole numbers. A null space of two dimensions says *More than one way to balance this — split it into separate
    equations*; an element on one side only *This can't be balanced — check the formulas (an element on only one side?
    `Cl`, `Br`)*; a token that is no formula *Can't read `XyZ` — is it a real formula?*. The answer is typeset
    (`EquationText`: coefficients bold, 1 left out, subscripts and charges) over *Balanced*; beside it the atom count
    table (each element, and *Charge* when ions are in it, on each side, ticked when equal) and the mass check (each
    side's total from the molar masses). Copy copies `2H2 + O2 → 2H2O`; Clear empties the field; four example chips.
    Kept: `balance.equation`. Pin: the field and the balanced line (`EquationBalancerCompact`, 400 points wide); Return
    or a press on the line opens the tool whole on it (carried: `equation`). Launch: `-SimplTool balance -SimplBalance
    "<equation>"` (the pin reads `-SimplBalance` too). Pictures (`chem_calc_tools`): `light-71-tool-balance`,
    `dark-71-tool-balance`, `light-71b-tool-balance-narrow` (780 × 700), `light-71a-tool-balance-cant` (Na + Cl2 → NaBr),
    `light-54l-pin-balance`.
  - **Molar Mass & Stoichiometry** (`moles`, `Tools/Chemistry/MolarMass.swift`; `/moles`, words *molar mass*,
    *grams*, *stoichiometry*, *limiting reagent*, *percent yield*, *empirical*): four tabs in a segmented control, the
    tab and each tab's inputs kept (`moles.tab`, `.formula`, `.convertFormula`, `.convertValue`, `.convertUnit`,
    `.equation`, `.given`, `.actual`, `.actualOf`, `.shares`, `.sharesInGrams`, `.empiricalMass`). *Molar mass*: a formula
    (chips Ca(OH)2, C6H12O6, CuSO4·5H2O) to its molar mass in g/mol, with the breakdown beside it — element, count,
    atomic mass, subtotal, mass % (the percent composition; `ChemMolar.breakdown`). *Convert*: a formula and one amount
    in grams, moles, molecules / formula units or litres of gas at STP (22.4 L/mol; Avogadro 6.02214076 × 10²³) to the
    other three (`ChemStoich.convert`). *Reaction*: an equation, balanced for you by the balancer's arithmetic (a
    "balanced for you" chip when the coefficients changed), a *Given* row per reactant (an amount, g or mol; blank
    is in excess), and the result — the limiting reagent, how far the reaction runs, each product's theoretical yield
    in g and mol, what is left of the other reactants (`ChemStoich.reaction`) — with an *Actual yield* field and a
    product picker for the percent yield. *Empirical*: each element's mass % (or grams) in rows, add or take one out, an
    optional molar mass: the empirical formula (moles over the smallest, times 2 to 6 until every ratio is within 0.08
    of a whole number) and the molecular one (`ChemStoich.empirical`). Copy on each tab. Pin: the Molar mass tab small
    (`MolarMassCompact`: a formula to g/mol; carried: `formula`). Launch: `-SimplTool moles -SimplMoles
    molar|convert|reaction|empirical`, `-SimplMolesFormula "<f>"`, `-SimplMolesEquation "<eq>"`, `-SimplMolesGiven
    "H2=4g,O2=16g"`, `-SimplMolesEmpirical "C=40,H=6.71,O=53.3"`, `-SimplMolesMass 180` (the pin reads
    `-SimplMolesFormula`). Pictures: `light-71c-tool-moles`, `dark-71c-tool-moles`, `light-71d-tool-moles-reaction`,
    `light-71f-tool-moles-narrow` (the Empirical tab, 780 × 700), `light-54v-pin-moles`.
  - **Gas Laws** (`gas`, `Tools/Chemistry/GasLaws.swift`; `/gas`, words *gas law*, *ideal gas*, *pv=nrt*, *boyle*,
    *charles*, *pressure*, *volume*): two tabs, kept with their values and units (`gas.tab`, `.ideal`, `.idealUnits`,
    `.combined`, `.combinedUnits`, `.constant`). *Ideal gas*: P, V, n and T, each with its own unit (atm, kPa, mmHg (Torr),
    bar, psi; L, mL, m³; mol or *g of…* a formula; K, °C, °F); the one left blank is solved in its field's unit
    (`ChemGas.ideal`, R = 0.082057 L·atm/(mol·K), the fields converted to atm, L, mol and K), the formula *PV = nRT*
    shown with the solved letter lit; two blanks say *Fill in all but one*, none *Leave one field blank*, a word
    which field, a temperature at or under 0 K why. *Combined*: P₁V₁/T₁ = P₂V₂/T₂ as two State cards with one unit
    picker per quantity, one blank solved (`ChemGas.combined`); chips *Boyle (T constant)*, *Charles (P constant)*
    and *Gay-Lussac (V constant)* leave that pair out and write the reduced law. Beside both, the piston
    (`GasPistonView`): a cylinder whose piston rises with the volume (a log scale, `ChemGas.pistonHeight`), dots as
    dense as the gas (n/V, or P/RT on the Combined tab; `pistonDots`) and a colour from cold blue to hot red
    (`pistonWarmth`), moving over 200 ms as a value changes (still with Reduce Motion). Copy copies the solved value.
    Pin: the Ideal gas tab small (`GasLawsCompact`, 360 points wide; carried: `values` as "P=1 atm,V=22.4 L,…"). Launch:
    `-SimplTool gas -SimplGas ideal|combined -SimplGasValues "P=1 atm,V=22.4 L,T=273.15 K"` (a value's unit sets its
    field's; `n=4 g H2` grams of a formula; the Combined tab's `P1 V1 T1 P2 V2 T2`), `-SimplGasConstant T|P|V` (a chip
    pressed); the pin reads `-SimplGasValues`. Pictures: `light-72-tool-gas`, `dark-72-tool-gas`,
    `light-72b-tool-gas-combined`, `light-72e-tool-gas-boyle`, `light-72a-tool-gas-narrow` (780 × 700), `light-54w-pin-gas`.
  - **Solutions & pH** (`solution`, `Tools/Chemistry/SolutionsPH.swift`; `/ph`, words *molarity*, *dilution*,
    *titration*, *buffer*, *acid*, *base*): four tabs, each keeping its inputs (`solution.tab`, `.molarity`, `.dilution`,
    `.ph`, `.titration`). *Molarity*: a solute's formula, an amount (mol, or g of the formula), a volume (mL or L) and a
    molarity, any two to the third (`ChemSolution.molarity`); beside it the mass % and ppm of a water-like solution
    (1.00 g/mL; `massPercent`, `ppm`). *Dilution*: M₁V₁ = M₂V₂ as Stock and Final cards, "How much stock? Leave the one
    you want blank", one blank solved and said in words (`dilution`; a final concentration above the stock's is
    refused). *pH*: one of pH, pOH, [H⁺] and [OH⁻] to the other three (`acidity`, Kw = 1.0 × 10⁻¹⁴ at 25 °C, said to be
    the only temperature here) on a 0–14 strip with a marker (`PHStrip`); beside it *From a concentration* — strong
    acid, strong base, weak acid (Ka) or weak base (Kb): water's own ions counted for a strong one, the weak one's
    quadratic solved exactly with its percent ionised (`fromConcentration`). *Titration*: strong acid–strong base,
    weak acid–strong base or weak base–strong acid; the analyte's concentration and volume, the titrant's
    concentration and the weak one's constant → the equivalence volume and the pH at the start, half-way (= pKa for a
    weak acid), at equivalence and at twice the volume, and the curve (`TitrationCurve`: 200 points to twice the
    equivalence volume, each the [H⁺] that balances the charges, found by bisection — `titrationPH`; the equivalence
    point marked); under it a *Buffer* card, pH = pKa + log([A⁻]/[HA]) (`bufferPH`). Copy on each tab. Pin: the pH tab
    small (`SolutionsPHCompact`, 340 points wide: one quantity to the other three on a strip; carried: `given`, `value`).
    Launch: `-SimplTool solution -SimplSolution molarity|dilution|ph|titration -SimplSolutionValues "…"` — the tab's
    fields as `key=value`: `formula=NaCl,amount=5.844 g,volume=100 mL[,molarity=]`; `M1=2,V1=50 mL,M2=0.5[,V2=]`;
    `pH=3` or `pOH=`, `H=`, `OH=`, with `kind=strong acid|strong base|weak acid|weak base,c=0.10,K=1.8e-5`;
    `kind=sa-sb|wa-sb|wb-sa,Ca=0.1,Va=25,Ct=0.1,K=1.8e-5[,pKa=,base=,acid=]` (the pin reads `pH=`/`pOH=`/`H=`/`OH=`).
    Pictures: `light-72c-tool-solution`, `dark-72c-tool-solution`, `light-72d-tool-solution-titration`,
    `light-72f-tool-solution-dilution`, `light-72g-tool-solution-narrow` (the Molarity tab, 780 × 700), `light-54x-pin-solution`.

- **The study and maths tools.** Tools of the Mac's own after the chemistry ones, each a `ToolKind` case with its
  name, line, symbol and colour inline (as the first tools have them), a tile at the end of its section's row, a popup,
  a pin with a quick version, a command in the search field, and its arithmetic in `Tools/Model` — Foundation only,
  compiled and checked by the Models job (`scripts/dev/swift-checks/StudyChecks.swift`) against the shared
  specification's test vectors, the same the Windows app is checked against. What they keep is `StudyPrefs`
  (UserDefaults `SimplStudy.<key>.<field>`, a picture's in a suite of its own); the Developer window's Interface page
  opens each and forgets what they keep. Every number shown is `StudyMath.text`: six significant figures at most,
  rounded half up, no float noise, a minus as "−".
  - **Word Counter** (`words`, *Study*; `Tools/WordCounter.swift`, `Model/WordCountModel.swift`): a text area, and
    beside it, live, the words, characters with and without spaces, sentences and paragraphs, the reading time (238
    words a minute) and speaking time (150), the average and the longest word; a target length with a bar and "12 to
    go" / "40 over"; the ten words used most as chips, the stop words left out (`WordCount`). Paste, Copy Counts (one
    line), Clear (⇧⌘⌫). A word is a run of letters and digits — an apostrophe or hyphen inside it keeps it one; a
    sentence ends at a run of `. ! ?` or an ellipsis; paragraphs are set apart by blank lines. The text (up to 200,000
    characters) and the target are kept. Pin: a small area and "5 words · 28 characters"; Open Full Screen carries the
    text (`takeInfo("text")`). Command `/words` (words such as "word count", "characters", "reading time", "essay").
    Launch flags: `-SimplTool words -SimplWordsText "…" -SimplWordsTarget 150`; a pin's `-SimplWordsText`. Pictures
    (`study_tools`): `light-73-tool-words`, `dark-73-tool-words`, `light-73b-tool-words-narrow`, `light-54m-pin-words`.
  - **Statistics** (`stats`, *Math & science*; `Tools/Statistics.swift`, `Model/StatsModel.swift`): four pages under
    a segmented control, the page kept (`StatsTab`). *Describe*: a data field (spaces, commas or new lines; a
    column pasted from a spreadsheet) → n, mean, median, modes, min, max, range, Q1, Q3, IQR (the quartiles the medians
    of the lower and upper halves, the median left out when n is odd — said under the results), sample and population
    SD and variance, SE, sum, sum of squares, the outliers past 1.5 IQR; a histogram (Sturges' bins) and a box plot
    (`Canvas`). *Regression*: x and y columns → slope, intercept, r, r², the equation, a scatter with the line, and
    ŷ from an x. *Distributions*: the normal's P(X < x), P(X > x), P(a < X < b), z and the inverse, the curve with
    P(X < x) shaded; Student's t's two- and one-tailed p and the critical t, both tails shaded. *Tests*: one-sample
    (data or n, mean, SD), two-sample Welch and paired t-tests → t, df, p (two- and one-tailed), the estimate, SE and
    the 95% CI. The arithmetic (`Stats`): erf by a power series to 2.5 and a continued fraction past it (within 1e-12
    of the C library's), the inverse normal by Acklam's approximation refined once, the t distribution by the
    regularized incomplete beta (Numerical Recipes' `betacf`) and its inverse by bisection. Every field is kept.
    Pin: a line of data → mean, median, sample SD; Open Full Screen carries it to Describe (`takeInfo("data")`).
    Command `/stats` ("statistics", "mean", "standard deviation", "regression", "z-score", "t-test"). Launch flags:
    `-SimplTool stats -SimplStats describe|regression|dist|tests -SimplStatsData "…"` (Regression and Tests take two
    columns set apart by `;`); a pin's `-SimplStatsData`. Pictures: `light-73c-stats-describe`,
    `dark-73c-stats-describe`, `light-73d-stats-regression`, `light-73e-stats-dist`, `light-73f-stats-tests`,
    `light-73g-stats-describe-narrow`, `light-54n-pin-stats`.
  - **Matrix Calculator** (`matrix`, *Math & science*; `Tools/MatrixCalculator.swift`, `Model/MatrixModel.swift`): two
    matrices A and B typed into grids of 1 to 6 rows and columns (steppers; Tab moves between cells; a block of rows
    pasted into any cell, or Paste, fills the grid — rows on lines or apart by `;`, cells by spaces, commas or tabs;
    `MatrixDraft`), an operation from a row of chips — A + B, A − B, A × B, k·A, Aᵀ, det A, A⁻¹, rank, RREF, trace,
    A·x = b (B the column b), the eigenvalues, Aⁿ — and the result: a matrix between brackets, a number, or the
    eigenvalues as λ₁… with a complex pair written once as a ± bi; the RREF's row operations in a disclosure, each
    with the matrix after it ("R₂ ← R₂ − 4R₁", "R₁ ↔ R₂", "R₂ ← R₂ ÷ 3"). Cells are exact fractions while what was
    typed is whole numbers, fractions or short decimals (`Fraction`, Int64 in lowest terms; a result past 62 bits goes
    on as a decimal — `MNum`), shown as fractions with a Fractions / Decimals toggle, decimals otherwise. What goes
    wrong is said in a line ("A × B needs A's columns to match B's rows — A has 3, B has 2", "A has no inverse: its
    determinant is 0", "No solution: the equations contradict each other", "Infinitely many solutions (1 free
    variable)"). Determinant and inverse by elimination, the eigenvalues by reduction to Hessenberg form and the
    shifted QR algorithm (EISPACK's hqr as Numerical Recipes gives it), real and complex, up to 6 × 6; the checks
    confirm each eigenvalue makes det(A − λI) vanish. A, B, the operation, k, n and the toggle are kept. Pin: a 2 × 2
    or 3 × 3 A with det A and A⁻¹ (its popover 380 wide); Open Full Screen carries A (`takeInfo("a")`). Command
    `/matrix` ("determinant", "inverse", "linear algebra", "system of equations", "eigenvalues", "rref"). Launch
    flags: `-SimplTool matrix -SimplMatrixA "1 2;3 4" -SimplMatrixB "…" -SimplMatrixOp add|subtract|multiply|scale|
    transpose|det|inverse|rank|rref|trace|solve|eigen|power -SimplMatrixSteps YES` (the RREF's steps open); a pin's
    `-SimplMatrixA`. Pictures: `light-74-tool-matrix`, `dark-74-tool-matrix`, `light-74b-tool-matrix-rref`,
    `light-74c-tool-matrix-solve`, `light-74d-tool-matrix-narrow`, `light-54o-pin-matrix`.

- **Compress Files (tools).** The Files group's fifth tool (`shrink`, `Tools/CompressFiles.swift`; `/compress` in the
  search field, or "shrink", "smaller", "file size", "upload limit"): pictures and PDFs made small enough for an upload
  box, on this Mac. Drop files or a folder (or Choose…), pick a target — *Max quality* (JPEG at 92%, pictures at their
  size), *Balanced* (80%, at most 2048 px on the long edge), *Smallest* (60%, 1280 px, PNGs to JPEG) or *Fit under … MB*
  — and Compress (Return) writes each file again to Downloads as `<name>-smaller.<ext>`, a free name, never over the
  original; the copies are shown in the Finder. Pictures (JPEG, PNG, HEIC, TIFF, WebP…) go through ImageIO: decoded,
  turned upright, scaled to the target's long edge and written as JPEG at its quality (a PNG with transparency stays a
  PNG, its transparency kept, unless the target is Smallest). PDFs keep their pages, text, links and notes and have their
  pictures re-encoded by PDFKit (`saveImagesAsJPEGOption`; at Balanced `optimizeImagesForScreenOption` too); at Smallest
  the pages are drawn into a new PDF through macOS's own *Reduce File Size* Quartz filter (`QuartzFilter`, each page's
  content and then its annotations; links do not survive the drawing). *Fit under* searches the quality down from
  Balanced's by halving, then smaller sizes (`ShrinkFit`, `Tools/ShrinkModel.swift`: Foundation only, checked by
  `scripts/dev/shrink-checks` by hand), and for a PDF tries Max, Balanced, Smallest in turn; when nothing lands under
  the limit the smallest reached is saved and the row says so. A copy that would be no smaller is not saved ("Already as
  small as it gets"). The table says each file's size before and after and the share saved (half up), a line under it the
  total, Copy copies it as text, and the first picture is shown before and after. A locked PDF, an unreadable picture
  and a file of another kind are said in their rows; nothing stops the rest. The pin: a small drop zone and the target
  (Balanced to begin with), opening the whole tool with the files (`CompressFilesCompact`; the target carried by
  `QuickCarry`, taken as `preset`). What it keeps (`ShrinkPrefs`: `SimplShrink.preset`, `.fit`; a picture's in a suite of
  its own). Launch flags: `-SimplTool shrink -SimplShrinkFiles "<path>,<path>" -SimplShrinkPreset max|balanced|smallest|fit`
  compresses the files as the tool opens (in a picture run the Finder is not brought up, and the console says what each
  came to: `[Simpl] shrink photo.jpg: 104 KB → 61 KB, 41% smaller`). Pictures (`files_tools`): `light-75-tool-shrink`,
  `dark-75-tool-shrink`, `light-75b-tool-shrink-narrow`, `light-54p-pin-shrink`.
- **Study Planner (tools).** The Study group's fifth tool (`plan`, `Tools/Planner/StudyPlanner.swift`; `/plan`, or
  "study plan", "schedule", "study time", "exam prep"): study blocks from the student's due dates. It reads the upcoming
  work the Dashboard, To Do and the Calendar read — the engine's `reminders` (three weeks of open work with its due
  dates, the student's own tasks among it), each piece's points and colour from the Dashboard's list (`dashList`, by
  id) and, past that list's eight days, from the Calendar's events (`calendar` over three weeks, matched by name and
  day) — and plans round it (`StudySource`). The planner itself is Foundation only (`Tools/Planner/Model/StudyPlan.swift`,
  checked by the Models job): a piece of work's need is its points (10 with none) times its kind's factor — an exam 3
  (a midterm, a final, a test or an exam by name counts as one whatever Canvas calls it), a quiz 1.5, an assignment 1,
  a discussion 0.5 — at 4½ minutes a point, rounded up to whole blocks (one at least, 24 at most); the blocks are spread
  evenly over the days from today to the due date less the days-before setting, never past the due time, each day's
  hours filled by priority (the earliest day to finish by first, then the soonest due, then the heaviest), a day's blocks ten minutes apart from the time of
  day chosen (morning 9, afternoon 2, evening 7; today no earlier than now, to the quarter hour). What will not fit is
  said ("2 won't fit"). Settings, kept (`PlanPrefs`: `SimplPlan.settings`, `.done`, `.view`; a picture's in a suite of
  its own): hours for each weekday (2 on a weekday, 3 at the weekend), the block (25/45/60/90 min, 45), the time of day,
  days before (1), and the courses to include. A week view — seven columns from today, a block "45 min · 2:00 PM", its
  course chip and its work, its number among its work's — and a list of every day with a block; each piece of work's
  progress under them (done, planned, won't fit, on a bar). A block ticked is done, kept on this Mac and taken off its
  work's need; *Regenerate* plans again round what is done; *Add to Calendar* saves the blocks left as `Study plan.ics`
  in Downloads (`StudyICS`, UTC times, an event a block) and shows it in the Finder — Calendar takes it with a
  double-click (EventKit was left out: it asks for calendar access and a prompt, for a file Calendar reads as it is).
  The pin: today's blocks as a checklist, ticked the same way (`StudyPlannerCompact`). Nothing is written to Canvas.
  Launch flags (when the launch opened it): `-SimplPlanHours 2` (that many hours every day), `-SimplPlanBlock 45`,
  `-SimplPlanDone "<item id>:2"` (that many blocks of that work done), `-SimplPlanView week|list`. Pictures
  (`planner_tool`): `light-75c-tool-plan`, `dark-75c-tool-plan`, `light-75d-tool-plan-list`, `light-75e-tool-plan-narrow`,
  `light-54q-pin-plan`.
- **Verb Conjugations (tools).** The Study group's sixth tool (`verbs`, `Tools/Verbs/VerbConjugations.swift`; `/verbs`,
  or "conjugate", "spanish", "french", "german", "italian", "portuguese", "tense"): any verb's main tenses in Spanish,
  French, German, Italian or Portuguese. The languages' data files — the rules for the regular classes, the stem
  changes (e→ie, o→ue, e→i…), the spelling changes (-car/-gar/-zar, -cer/-ger/-yer/-eler…), the compound and
  composed tenses, a table of the irregular verbs (the sixty-odd most common and more) and a list of common verbs —
  are `Resources/Verbs/<lang>.json`, the same files the Windows app reads, bundled by the synchronized folder as
  `PhoneHandIn.html` is and read once each (`VerbStore`). Their schema, the reference engine and the generators that
  write them are `scripts/dev/verbs/` (`README.md`, `check.mjs`, `gen/gen-<lang>.mjs`): `node scripts/dev/verbs/check.mjs
  mac/Simpl/Resources/Verbs` validates the bundled files and runs the reference's test vectors; a change is made in a
  generator and written with `VERBS_OUT=mac/Simpl/Resources/Verbs node scripts/dev/verbs/gen/gen-es.mjs` (the
  generators write the bundled files byte for byte). The engine (`Tools/Verbs/Model/VerbEngine.swift`, Foundation only, checked by the Models job against
  the data files and the reference's test vectors) follows the reference step for step: an irregular verb's forms from
  its table, else the infinitive's stem and the class's endings with the stem and spelling changes, a compound tense
  from its auxiliary and participle, a composed one (an imperative) from other tenses' forms, a separable German prefix
  sent to the end; `table` says which forms are irregular (other than what the rules alone give) for tinting. The tool:
  the language (chips, remembered), the verb's field with suggestions from the data's verbs as it is typed, *Highlight
  irregular forms* (tinted in the tool's colour, bold), *Copy Table* (every tense as text), and the tenses as cards under
  their groups' headings — the pronouns beside the forms, "—" where a person has none (the first-person imperative), a
  participle or gerund as one form. A verb the rules cannot read is said inline ("Can't conjugate “xyz” — is it an
  infinitive? Try hablar, comer, vivir."). What it keeps (`VerbPrefs`: `SimplVerbs.lang`, `.verb`, `.highlight`; a
  picture's in a suite of its own). The pin: the language, a verb and its present tense (`VerbConjugationsCompact`;
  Return or Open Full Screen opens the whole tool on them, carried as `lang` and `verb`). Launch flags: `-SimplVerbs
  es|fr|de|it|pt`, `-SimplVerb "<infinitive>"`. Pictures (`verbs_tool`): `light-76-tool-verbs` (Spanish, tener),
  `dark-76-tool-verbs` (French, aller), `light-76b-tool-verbs-narrow` (German, anfangen), `light-54r-pin-verbs`.

## Data and encryption

(1.3.76) Simpl for Mac keeps what it holds secret in the Mac's own **Keychain**, which macOS encrypts under the
student's login password (`Support/Vault.swift`):

- **The saved sign-in and the school's cookies** are generic-password items of their own in the login Keychain
  (services *com.simplcourses.ios.login* and *com.simplcourses.ios.cookies*), as on the iPhone and as before 1.3.76
  (`LoginVault` in `Web/LoginAssist.swift`, `Web/CookieJar.swift`).
- **The rest of what Simpl keeps** — the extension's storage (`Application Support/SimplCourses/storage.json`:
  settings, grade history, goals), the answers kept for a fast start (`Caches/Answers/<school>/`, each file named by an
  HMAC, so a name says nothing of what it holds) and the flashcards
  (`Application Support/com.simplcourses.mac/flashcards.json`) — is sealed by Simpl: AES-256-GCM under one 256-bit key
  made on first use, with what the file is authenticated alongside (a file cannot pass for another). That key is one
  more generic password in the login Keychain (*Simpl Courses vault*, account `master-<id>`);
  `Application Support/Simpl/vault.key` only names the item.

The items carry macOS's own access list: the app that made them reads them without a prompt, and any other app that asks
for one makes macOS ask the student first. Released builds are signed with the team's Developer ID (Releasing, below),
so every new version is that same app to the Keychain and an update reads the items with no prompt. Only a development
or CI build, signed just to run locally, is a different app each time it is built: macOS may ask once whether it may
use each item ("Simpl wants to use your confidential information stored in … in your keychain": *Always Allow*). There
is no Touch ID or password prompt of Simpl's own: that needs the data-protection keychain, and with it a provisioning
profile and keychain entitlements Simpl's builds do not carry. Settings ▸ Data ▸ Encryption says where the key is.

Only where the Keychain will not take the key (a CI runner) is it kept in `vault.key` itself, readable by its owner
only, which locks nothing: the console says so every launch, and Settings ▸ Data says the key is in a file.

A file written before 1.3.76 is read once as it was and written back sealed (the kept answers are dropped instead: they
are only a cache). A Mac that ran an early 1.3.76 build — which kept the sign-in and the cookies inside Simpl, sealed,
as `Application Support/Simpl/login.bin` and `cookies.bin`, and held the vault's key with the Secure Enclave — has the
two files put back into the Keychain once and deleted (one already in the Keychain wins), and its key opened by the
Enclave one last time, put into the Keychain, and `vault.key` written again without the Enclave key's blob. A vault that
cannot be opened — a `vault.key` from another Mac, its Keychain item gone — is set aside with what it sealed in
`Application Support/Simpl/Unreadable-<date>/` (never deleted) and a new one made, with a line on the console and no
alert; a Keychain that will not give the key this launch (access refused) leaves everything as it is until the next.
Signing out deletes the sign-in and cookie items. **Reset Everything** deletes them and the vault's key item, so nothing
it sealed can be read again; the flashcards, which Reset has never erased, are sealed again under the new key.

Plain on purpose: the preferences (UserDefaults: the theme, the layout, what was seen), the console, the theme's photos
(`Application Support/Simpl/Photos`), the screenshot suite's pictures, files the student exports or saves elsewhere, and
WebKit's own website data.

**What it protects, and what it does not.** It protects data at rest from anyone without the student's login password:
files copied off the Mac, another account or an administrator browsing the disk, a lost or stolen disk. The login
Keychain is itself a file in the same home folder, locked with the login password, so a backup or Time Machine copy of
the home folder holds the sealed files and their key together, and whoever also has the login password can open both,
as they could every other password in that Keychain. While the student is logged in the Keychain is unlocked: another
program running as them cannot take the key without macOS asking first (for a Developer ID build), but the student can
let it, and it can read what Simpl does not seal, WebKit's website data (the school's session among it) included. A key
kept in `vault.key`, the last resort, locks nothing.

## Building and running

```
python3 scripts/dev/make-mac-project.py   # after adding a shared file
open mac/Simpl.xcodeproj                  # scheme Simpl, My Mac
```

It runs signed to run locally (no team needed). Against the mock Canvas:

```
node scripts/dev/mock-canvas.mjs 8800 8801 &
Simpl.app/Contents/MacOS/Simpl -SimplBaseURL http://localhost:8800 -SimplDemo YES -SimplPlace todo
```

Launch arguments (each read once): `-SimplPlace dashboard|courses|todo|calendar|grades|notifications|inbox|groups|
search:<q>|course:<id>|group:<id>|section:<ctx>:<kind>`, `-SimplPush <Canvas address>`, `-SimplOpen quiz:<course>:<quiz>
[:take:<n>|:review] | tool:<course>:<id> | settings | setup | newtask | compose | whatsnew | handin | tour[:<n>] |
touroffer | preview[:counter] | more:history | more:help | more:tool` (`preview`: the first row on screen opens its
card; `preview:counter`, with `-SimplSheet <counter>`: the first row of that counter's panel; the `more:` three: More
from Canvas's popups, and its first tool's window), `-SimplSidebarScroll more` (the sidebar scrolled to More from
Canvas), `-SimplSetupFirstRun YES` (with `-SimplOpen setup`: the setup as a first run draws it, every course ticked
and Set Up Later), `-SimplShowLoggingIn YES` ("Logging you in" over the window, "Still signing in…" after a moment),
`-SimplPreviewFail YES` (with `-SimplPickerConfirm YES`: "Is this your school?" with a preview that could not load),
`-SimplCalView week|list` (the Calendar opened on that view), `-SimplWhatIf YES` (a course's grades with What-If on,
nothing typed), `-SimplAttach <file>` (with `-SimplOpen compose`: New Message with that file attached, uploaded to the
mock), `-SimplAppearance light|dark`, `-SimplSettingsTab notifications|grades|data|updates|about` (the tab
Settings opens on), `-SimplRolledBackFrom <version>` (1.3.86: Settings ▸ Updates as after a version was put back, the
hold's row under Version), `-SimplSidebar hidden` (the window opens with its sidebar folded away),
`-SimplMorphHold <share>` (with `-SimplSheet <counter>`: the counter's tile grows that share of the way into its
panel, 0.5 half-way, and stays there — the real grow, stopped, so a picture shows it under way, even where the Mac
reduces motion; `DashMorphHold`), (tools) `-SimplTool balance -SimplBalance "<equation>"` (the Equation Balancer on
that equation), `-SimplTool moles -SimplMoles <tab> -SimplMolesFormula|-SimplMolesEquation|-SimplMolesGiven|
-SimplMolesEmpirical|-SimplMolesMass …` (Molar Mass & Stoichiometry on a tab with its inputs), `-SimplTool gas
-SimplGas ideal|combined -SimplGasValues "P=1 atm,V=22.4 L,T=273.15 K" [-SimplGasConstant T|P|V]` (Gas Laws on a tab
with its fields, one blank to solve), `-SimplTool solution -SimplSolution molarity|dilution|ph|titration
-SimplSolutionValues "pH=3"` (Solutions & pH on a tab with its fields; the forms are in its section). In the pictures' consoles a counter opened says how it came (`[Simpl] counter: next
grows`, or `fades in (Reduce Motion)`).

## Releasing

Simpl for Mac has its own version line (`MARKETING_VERSION` and `BUILD` in `scripts/dev/make-mac-project.py`; 1.0 was
the first; 1.1, build 2, with the interface's 2.99.23, runs on Brightspace too — `docs/brightspace.md`). A release is made by hand: **Actions → Simpl for Mac release → Run workflow**
(`.github/workflows/mac-native-release.yml`, `scripts/release-mac-native.sh`). It builds the app for Apple silicon and
Intel, signs it with the team's Developer ID (the same five secrets the Safari app's release uses, `docs/mac-app.md`),
has Apple notarize it, staples the ticket and publishes `Simpl-Mac-<version>.zip` as a GitHub Release of its own,
`mac-v<version>`. A version already released is refused, never replaced: raise the version (and the build) first and
run `python3 scripts/dev/make-mac-project.py`. The site's Mac download stays the Safari app's; Simpl for Mac has an
update feed of its own (Updates, below). (1.3.82) A beta of a commit the Mac app workflow has finished with is not
compiled again: that run built and signed its app ahead, and the release only notarizes, staples and publishes it once
it has checked it (Betas, below).

`simpl://` links open in the app: `simpl://courses/101/assignments/1001` opens that assignment.

## Updates

Simpl for Mac keeps itself up to date (`Support/Updater.swift`, a port of the Safari app's `Updater.swift`).

- **The feed** is `https://simplcourses.com/app/mac-latest.json` (an Info.plist `SimplUpdateFeed` names another, for a
  test): `{ "version": "1.2", "url": "<the zip>", "sha256": "<hex>", "size": 41404983, "notes": "…",
  "published": "<ISO 8601>", "minimumOS": "14.0" }`. Only `version` and `url` are required. A version newer
  than the app's `CFBundleShortVersionString` is offered unless its `minimumOS` is above the Mac's macOS.
- **Betas (1.3.17).** *Settings ▸ Updates ▸ Get beta updates* reads `https://simplcourses.com/app/mac-beta.json`
  instead (the usual feed when there is none yet): the newest `mac-v*` release of all, a beta or not, with
  `"beta": true` when it is one. A beta is released with the release workflow's **beta** switch on: a GitHub
  pre-release, *Simpl for Mac <version> Beta*, which the usual feed passes over (`scripts/publish-site.sh` writes both
  feeds). Turned off, a Mac stays on the beta it has until a release newer than it comes. Earlier Versions marks betas.
  (1.3.31) A beta builds quickly: it is compiled a file at a time (`SWIFT_COMPILATION_MODE=singlefile`, still
  optimized) from the last beta's intermediates (its own cache, `mac-release-beta-…`), so only what changed is compiled;
  a release proper is still compiled whole-module from its own (`mac-release-stable-…`). (1.3.82) The Mac app workflow
  builds a beta's app ahead on every push, beside its pictures, signed as the release signs it, and keeps it by a
  cache keyed by the commit (`mac-release-app-beta-<sha>`), with each push's intermediates for the next
  (`mac-release-beta-<sha>`). A beta released on a commit whose run has finished that job ("Models, and the release
  app built ahead") takes that app once it is checked — this commit, version, build and kind; the team's Developer
  ID, the hardened runtime, a secure timestamp, both kinds of Mac — and is only notarized, stapled and published,
  about a minute and a half. Dispatched sooner, it compiles the app itself from the newest push's intermediates, as
  before but with one push's changes to compile.
- **When.** At launch when the last check is more than an hour old, then every hour (looked at every five minutes and
  whenever the app comes to the front, so a Mac asleep does not stretch the wait), and on **Simpl ▸ Check for
  Updates…** or *Check Now* in **Settings ▸ Updates**. (1.3.102, every minute since 1.3.109) **Critical Updates every minute**: each time
  the hour is not up, and a few seconds after the Mac wakes, when the feed was last asked four minutes ago or more
  (timed from when the asking began, answered or not, so a slow answer never skips a tick and a feed that fails is
  not asked again at every return to the app), the app reads the feed it follows (the beta feed on the beta channel, which names `criticalVersion` as the usual one
  does) for a Critical Update alone (`Updater.scanForCritical`): without the cache, a ten-second timeout, nothing
  shown. Only when it names one newer than this copy, that this Mac runs, does the usual check run, and the Critical
  Update goes in at once with its notice. None while a check, a download or an install is under way or a Critical
  Update's notice is up; one timer does it all. The menu opens Settings on Updates when there is a version
  to install or the check failed, and says *up to date* otherwise.
- **Installing.** *Update Now* installs at once. Otherwise an update installs itself — always: (1.3.86) there is no
  *Install updates automatically* switch any more, and a Mac that had it off has it on again (its old `autoUpdate`
  key is removed at launch) — at a quiet moment only: no quiz, external tool, sheet, popup or modal panel open, so a
  quiz or a message is never cut off by the relaunch.
- **Checks.** The download must answer 200, start with `PK` (a zip, not a web page) and match the feed's SHA-256; it
  is unpacked with `ditto -x -k`; the app inside must have this app's bundle id and pass `codesign --verify --deep
  --strict`, against this copy's own designated requirement when this copy is Developer ID signed (so only the same
  team's build can replace it). It then takes this copy's place, which goes to the Bin, or goes to the Applications
  folder when this copy cannot be replaced where it is (App Translocation, a folder that cannot be written); the
  quarantine mark is removed, LaunchServices is told, and the new copy opens as this one quits.
- **Earlier versions.** Settings ▸ Updates lists the `mac-v*` GitHub Releases (`Simpl-Mac-<version>.zip`, with the
  SHA-256 GitHub keeps as the asset's `digest`); *Install…* puts one back through the same checks. (1.3.86) So that
  the next check does not bring the newest straight back, it keeps a hold first: the version it replaces (or one held
  already, if newer) as `SimplRolledBackFrom` in the app's defaults. The version put back reads it as it opens and
  stays: a version found up to the one held is offered (*Update Now*) but never installs itself; one newer than it
  installs itself as usual, *Update Now* ends the hold, and a Critical Update goes in all the same. Settings ▸ Updates
  says so under Version ("Staying on 1.3.84 — You put this version back. Update Now installs 1.3.85; …"), and the
  hold is forgotten once the app is that version or newer. A version from before 1.3.86 knows no hold: for it the
  old `autoUpdate` switch is also written off, as before.
- **Never in a development run**: launched with `-SimplBaseURL`, `-SimplDemo` or `-SimplShotFile`, run from
  DerivedData or a `Build/Products` folder, or a Debug build. The app neither checks nor installs then.

## CI

`.github/workflows/mac.yml` builds the app with Xcode 26 on every change to `mac/` or the shared files, runs it against
the mock Canvas, and pictures every screen in light and dark (`scripts/dev/mac-shots.sh`). The pictures, the build's
errors and warnings and the built app go to the `mac-shots` branch. Nothing is published. (1.3.82) Beside the
pictures a beta's app is built and signed with the Developer ID ahead of a release and kept for it by a cache, never
notarized, published or downloadable here (Releasing, above).
(1.3.80) Every run, a push's too, takes the whole set — 423 pictures, what a push and "full" took before — in four
parts on four Macs at once, each building the app from the last build and taking the pictures `mac-shots.sh` shares
out to it by the time each took (`scripts/dev/baselines/mac-shot-times.txt`); a Linux job puts the parts together,
checks them and pushes them to `mac-shots` as one run's; the model checks run beside the parts (docs/CI.md). Which part
takes which picture is in `shot-plan.txt` on `mac-shots`; `SHOTS=plan SHARDS=4 bash scripts/dev/mac-shots.sh` writes
it on any machine without the app.
(1.3.76) After the pictures, every storage, flashcards and kept-answer file the app wrote must start with the vault's
`SIMPLENC` mark, and no `login.bin` or `cookies.bin` may be found (the sign-in and the cookies are Keychain items: Data
and encryption, above), or the run fails. Where each worker's vault keeps its key (`vault.key`'s mode) is printed:
`keychain`, or `file` when the runner's Keychain would not take it.
(1.3.80) Live checks are pictured last in their part, one at a time (a check runs only while its app is in front) and against a mock
of their own on :8810 (its changes never show in another picture): `light-48a-live-dashboard` (List; the soonest work
marked done 6 seconds in), `light-48b-live-cards` (Cards; a course's total moved), `light-48c-live-todo` (a task added)
and `light-48d-live-course` (a course's assignments at their own pace), each checked every 4 seconds and pictured after
its fifth check (third for the course). `live_check` reads their consoles into `live-check.txt`, and the step "Live
checks find what changed" fails the run on a FAIL line: each Dashboard check that found nothing new said `· 0 redraws`,
the change was found and put in place as `counters` and `list` (`cards` in Cards, `todo` on To Do), the check after it
found nothing new, the course's assignments were checked, and no check went unanswered.
(1.3.78) Every screen's open is timed on the console — `[Simpl] open Grades: 14 ms (cached)`: from the press that
opened it (a sidebar row, a link, Back) to the frame with its content, and where that content came from (`cached`, this
session's answer in memory; `kept`, an answer kept from before, its counts held back until the `live` one is in;
`fetched`, asked of the page while the screen waited; `alive`, a screen never put away) — and every run takes
`light-01w-walk`: the app walks through its places as a student's clicks would (`-SimplWalk`, Support/OpenClock.swift),
each root place, a course and an assignment and then each again, so its console has the time of every first visit and
every visit again; last, the assignment opened from the Dashboard (its course's place, made with it on top) and Back,
where the console must say `open Assignment`, not the course's list. (1.3.102) `light-01x-sidebar-again` walks
`on:/courses/101/assignments/1001,again` — the assignment pushed on the Dashboard, as Hand In pushes one, then the
Dashboard's row pressed again — and `again_check` (mac-shots.sh, SHOTS=check) fails the run unless its console says
`walk: at dashboard, 1 pushed` and then `walk: at dashboard, 0 pushed`. `light-01v-walk-plain` walks the same way beside
it, last in their part and by themselves (the copies picturing the rest would slow them, and unevenly), with the screens loading as they did before 1.3.78 (`-SimplPlain`: nothing
held or read ahead, the Dashboard not kept), so the two consoles compare like for like — both against a Canvas a
quarter of a second away, as a school's is (its assignment lists 0.6 s; the mock's `apiDelay`, as the web's fluid suite
has it), not the mock's instant answers. In the screenshot suite and in
developer mode the console also times each call (`call grades: 820 ms`, `read ahead home: 40 ms`) and when each screen
appeared (`appear Grades: 12 ms`), and a stack that sets its own path (`stack: grades set its own path, 1 screens
to 0` — only its own Back should). Each picture is taken with its window brought to the front 0.7 s before
(Shot.swift): a copy another had covered drew nothing new, so an animation under way in it was pictured where it had stopped. A
window the window server gives back empty is asked for again, half a second apart, four times at most.
