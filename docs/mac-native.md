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
    Shell/Commands.swift        the menu bar: New Task / New Message, Reload, the Go menu, Change School, What's New
    Shell/LoginLayer.swift      the sign-in card, "Logging you in", "Stay logged in?", Start Over
    Shell/SchoolPicker.swift    the first run: search for the school by name (↑ ↓ Return, as Spotlight)
    Shell/SettingsView.swift    Simpl ▸ Settings: General, Notifications, Grades, Data, Updates, About
    Shell/ToolWindow.swift      an external tool (or a page Simpl does not draw) in a window of its own
    Shell/WhatsNewSheet.swift   what changed, after an update
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
  it opens to its sections.
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
- **The Dashboard's counters (1.3.78).** On a window 1024 points wide or wider — the sidebar out or not — the six
  counters stand in one row of six, each a compact tile about 72 points tall: its name, then its number with its line
  beside it (whole where it fits, ending in an ellipsis where a few words of it still fit, else left out — never a
  third line). Each symbol is beside its name where every name in the row has room for one (measured as the tile draws
  it), else in the tile's corner beside its number; a name is never cut short. A narrower window keeps tall tiles,
  three across (two on a page under 520 points). The rule reads the window's own width (`DashboardView.rowOfSix`,
  `WindowWidthProbe`), not the page's, so showing the sidebar or the side column coming in never sends the row back to
  two. A tile's panel still opens over the page from its tile: the first from its left edge, the last from its right,
  one between them in step, each kept on the page.
- **The Dashboard and the window, after the UI review (the home findings; the same on Windows).** The view (Cards,
  List, Activity) and a labelled New Task sit in the page by the greeting, so a tool pinned to the toolbar never pushes
  them into its » menu; on a one-column page the view chosen comes straight under the counters. Each row of tiles shows
  every counter's full name ("Announcements" with "3 unread · 2 courses", "Graded this week") where every one fits as
  the tile draws it, else every short one with the engine's note ("Unread", "announcements · 2 courses") — never a mix,
  never a name cut; tooltips and VoiceOver say the count in words ("3 unread announcements"). Red is for late work only:
  Overdue with work in it wears a red wash and edge of its own (not the glass's tint, which macOS mutes in a window in
  the background), Due today the accent. While the page shows what was kept from last time, the numbers stay, faint, and
  the date line says "Updating…". A counter's panel lists its work as every Dashboard list does (course and kind over
  the title, time over points on the right), about six rows before it scrolls, a row's preview opening under it in the
  panel, and ends with "See All 14 in To Do" (Grades, Notifications). Course cards: a 48-point band with the sidebar's
  name, the term and teacher under it, "2 new" announcements, quick links named where the card is wide. A colour worn
  marks the sidebar's chosen row's glyph and the controls — never the words or the titles — and a drawn scene stays
  faint behind the sidebar's rows and out of a tile's top. Typing in Search answers in its suggestions only; the page
  changes on Return (a sum's Return copies its answer). The tour is six steps (the counters, Grades, a course, Tools,
  Search, then Report a Bug at the account menu), with Skip Tour, ✕ and Escape, a Next that always works, and the whole
  window dimmed, toolbar too, its buttons resting meanwhile. A quick look is a light-dismiss card with nothing dimmed
  behind it, as Windows' flyout.
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
  (1.3.53) Two glass panels, the question's words and its answer (a lesson: one): the hook tells the app each kept
  part's question and answer boxes (the card's children before and from the one holding Learnosity's box, which gets a
  gap above it — `data-simpl-answer`) in the page's own coordinates, and the page's height. The app sizes the page to
  that height inside its own scroll view, a native glass panel behind each box, and the page hands its scrolling to the
  popup (`AltaWebView.forwardsScroll`); a new question scrolls back to the top. Every button in the parts' button bars
  (`[class*=button-bar]`), whatever it says, goes to the app — its words, Alta's main one (`primary` in its class),
  whether it can be pressed — and the bars are put away (`data-simpl-bar`): the app shows them as its own glass buttons
  (the main one prominent, ⌘↩) and presses Alta's (`__simplButtons.press`). A sprite sheet kept for its symbols makes
  the parts round it that hold nothing else kept take no room (`data-simpl-thin`): Alta's banner held one.
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
  esters, the cis kink and head and tails. *Open in Molecule Builder* hands the atoms to the Periodic Table's builder,
  which keeps their shape (`MoleculeModel.load(handOff:)`, `fixedShape`: no VSEPR). Launch flags for pictures:
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
  (1.3.70) Alta: a graph in what a question asks, larger than 300 × 220, is drawn small (`data-simpl-mini`, a CSS zoom
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
  own): Overview (versions, where the app runs, live diagnostics), Updates (state, feed, beta / automatic / pause, a
  feed address of its own, Check and Install Now), Engine (its state, Refresh, Reload, Forget Kept Answers, and a
  console: any `BCVNative.call` with JSON arguments, its answer pretty-printed), Alta & Macmillan (state, Save Page
  Code, Error Logs — Alta's Error Logs button shows only in developer mode now — and the graphs' light colours),
  Interface (What's New, the tour, the setup, their resets, the look for this run, the main window's size) and
  Preferences (every one the app keeps, filtered, removed one by one; its folders). Critical Updates: the release
  workflow's `critical` switch publishes a release proper titled "Critical Update" with a mark in its notes;
  publish-site.sh names the newest one in both feeds (`criticalVersion`), and a Mac on an older version installs the
  feed's newest at its next check (Updater.swift) — automatic updates off, paused, a version put back, a quiz open:
  nothing holds it back — with a notice over the window while it installs, tried again every ten minutes if it fails.
  Only copies from 1.3.72 on know to do this.
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
  screenshot suite (`light-55k-ptable-cloud`, `dark-55l-ptable-cloud`); `-SimplCloudCheck YES` writes the model's test
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
  only module opens by itself, and a locked one says when it opens under its name, closed. A tool in a module, a tool's
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
  Focus timer, Citation generator, Grade needed), *Math & science* (Calculator, Graphing calculator, Unit converter,
  Periodic table), *Files* (File converter, Merge & split PDFs, PDF annotator, Image to text) and *Biology*, as on
  Windows — each tile's line one short line, three tiles across from about 900 points of page (`ToolGroup`, `ToolGroupSection`,
  `ToolTile.columns`). An open tool has one pin control, in its heading, named for what a press does (*Unpin* / *Pin to
  Toolbar*); the window's toolbar no longer adds a second. The toolbar holds three tools at most: past that, two and a
  *Tools* pull-down with the rest (`ToolsCenter.toolbarSplit`, `MorePinsMenu`; while a session runs, the timer — its
  pin as wide as two — and the pull-down), so a screen's own buttons keep their room — the same in a tool window and on
  a popup's bar (`PopupPins`). A tool picked in the pull-down opens its quick version in a popover under it.
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
  Tour, a ✕, Back and a Next that always works; Esc ends it (the sheet's Done gives Escape up while it is on); ended
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
    the window comes back to it. It reads again only when it is shown and what it holds is stale or something was done
    meanwhile, each card changing in place. Its title stays in the stack (`DashboardChrome`; its view and New Task are
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
touroffer`, `-SimplSetupFirstRun YES` (with `-SimplOpen setup`: the setup as a first run draws it, every course ticked
and Set Up Later), `-SimplShowLoggingIn YES` ("Logging you in" over the window, "Still signing in…" after a moment),
`-SimplPreviewFail YES` (with `-SimplPickerConfirm YES`: "Is this your school?" with a preview that could not load),
`-SimplCalView week|list` (the Calendar opened on that view), `-SimplWhatIf YES` (a course's grades with What-If on,
nothing typed), `-SimplAttach <file>` (with `-SimplOpen compose`: New Message with that file attached, uploaded to the
mock), `-SimplAppearance light|dark`, `-SimplSettingsTab notifications|grades|data|updates|about` (the tab
Settings opens on), `-SimplSidebar hidden` (the window opens with its sidebar folded away).

## Releasing

Simpl for Mac has its own version line (`MARKETING_VERSION` and `BUILD` in `scripts/dev/make-mac-project.py`; 1.0 was
the first; 1.1, build 2, with the interface's 2.99.23, runs on Brightspace too — `docs/brightspace.md`). A release is made by hand: **Actions → Simpl for Mac release → Run workflow**
(`.github/workflows/mac-native-release.yml`, `scripts/release-mac-native.sh`). It builds the app for Apple silicon and
Intel, signs it with the team's Developer ID (the same five secrets the Safari app's release uses, `docs/mac-app.md`),
has Apple notarize it, staples the ticket and publishes `Simpl-Mac-<version>.zip` as a GitHub Release of its own,
`mac-v<version>`. A version already released is refused, never replaced: raise the version (and the build) first and
run `python3 scripts/dev/make-mac-project.py`. The site's Mac download stays the Safari app's; Simpl for Mac has an
update feed of its own (Updates, below).

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
  a release proper is still compiled whole-module from its own (`mac-release-stable-…`). A beta needs no wait for the
  Mac app workflow: the release builds the app itself, so it is dispatched as soon as the change is pushed, and the
  screenshots come alongside.
- **When.** At launch when the last check is more than four hours old, then every four hours (looked at every ten
  minutes and whenever the app comes to the front, so a Mac asleep does not stretch the wait), and on **Simpl ▸ Check
  for Updates…** or *Check Now* in **Settings ▸ Updates**. The menu opens Settings on Updates when there is a version
  to install or the check failed, and says *up to date* otherwise.
- **Installing.** *Update Now* installs at once. With *Install updates automatically* on (the default) an update
  installs itself at a quiet moment only: the app not in front and no sheet open, so a quiz or a message is never cut
  off by the relaunch.
- **Checks.** The download must answer 200, start with `PK` (a zip, not a web page) and match the feed's SHA-256; it
  is unpacked with `ditto -x -k`; the app inside must have this app's bundle id and pass `codesign --verify --deep
  --strict`, against this copy's own designated requirement when this copy is Developer ID signed (so only the same
  team's build can replace it). It then takes this copy's place, which goes to the Bin, or goes to the Applications
  folder when this copy cannot be replaced where it is (App Translocation, a folder that cannot be written); the
  quarantine mark is removed, LaunchServices is told, and the new copy opens as this one quits.
- **Earlier versions.** Settings ▸ Updates lists the `mac-v*` GitHub Releases (`Simpl-Mac-<version>.zip`, with the
  SHA-256 GitHub keeps as the asset's `digest`); *Install…* puts one back through the same checks, turning automatic
  updates off first so the next check does not bring the newest straight back.
- **Never in a development run**: launched with `-SimplBaseURL`, `-SimplDemo` or `-SimplShotFile`, run from
  DerivedData or a `Build/Products` folder, or a Debug build. The app neither checks nor installs then.

## CI

`.github/workflows/mac.yml` builds the app with Xcode 26 on every change to `mac/` or the shared files, runs it against
the mock Canvas, and pictures every screen in light and dark (`scripts/dev/mac-shots.sh`). The pictures, the build's
errors and warnings and the built app go to the `mac-shots` branch. Nothing is signed for distribution or published.
(1.3.76) After the pictures, every storage, flashcards and kept-answer file the app wrote must start with the vault's
`SIMPLENC` mark, and no `login.bin` or `cookies.bin` may be found (the sign-in and the cookies are Keychain items: Data
and encryption, above), or the run fails. Where each worker's vault keeps its key (`vault.key`'s mode) is printed:
`keychain`, or `file` when the runner's Keychain would not take it.
(1.3.78) Every screen's open is timed on the console — `[Simpl] open Grades: 14 ms (cached)`: from the press that
opened it (a sidebar row, a link, Back) to the frame with its content, and where that content came from (`cached`, this
session's answer in memory; `kept`, an answer kept from before, its counts held back until the `live` one is in;
`fetched`, asked of the page while the screen waited; `alive`, a screen never put away) — and both picture sets take
`light-01w-walk`: the app walks through its places as a student's clicks would (`-SimplWalk`, Support/OpenClock.swift),
each root place, a course and an assignment and then each again, so its console has the time of every first visit and
every visit again; last, the assignment opened from the Dashboard (its course's place, made with it on top) and Back,
where the console must say `open Assignment`, not the course's list. `light-01v-walk-plain` walks the same way beside
it, last and by themselves (the copies picturing the rest would slow them, and unevenly), with the screens loading as they did before 1.3.78 (`-SimplPlain`: nothing
held or read ahead, the Dashboard not kept), so the two consoles compare like for like — both against a Canvas a
quarter of a second away, as a school's is (its assignment lists 0.6 s; the mock's `apiDelay`, as the web's fluid suite
has it), not the mock's instant answers. In the screenshot suite and in
developer mode the console also times each call (`call grades: 820 ms`, `read ahead home: 40 ms`) and when each screen
appeared (`appear Grades: 12 ms`), and a stack that sets its own path (`stack: grades set its own path, 1 screens
to 0` — only its own Back should). Each picture is taken with its window brought to the front 0.7 s before
(Shot.swift): a copy another had covered drew nothing new, so an animation under way in it was pictured where it had stopped. A
window the window server gives back empty is asked for again, half a second apart, four times at most.
