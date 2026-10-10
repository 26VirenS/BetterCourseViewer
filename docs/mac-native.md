# Simpl for Mac (native)

Simpl for Mac is a Mac app of its own, not the Safari extension's container app (that one is `macos/`, see
[mac-app.md](mac-app.md)). It is built the way the iPhone app is: the student's own Canvas runs in a web view with the
extension injected into it, signed in on the school's real sign-in page, and that web view is the **engine underneath**.
Over it, the window is the Mac's own: a sidebar, a unified toolbar with Back, Forward and Search, the menu bar, sheets,
popovers, context menus, Quick Look, a Settings window and a window for each external tool. Every number on screen is
asked of the page (`BCVNative.call`, `extension/content/app/native-app.js`), exactly as on the iPhone, so the Mac, the
iPhone and the web count the same things the same way.

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
    Shell/RootView.swift        the engine underneath, the window over it, sign-in, sheets, Quick Look
    Shell/MainShell.swift       the split view: the sidebar (places, courses and their sections, groups, the account)
    Shell/Commands.swift        the menu bar: New Task / New Message, Reload, the Go menu, Change School, What's New
    Shell/LoginLayer.swift      the sign-in card, "Logging you in", "Stay logged in?", Start Over
    Shell/SchoolPicker.swift    the first run: search for the school by name (↑ ↓ Return, as Spotlight)
    Shell/SettingsView.swift    Simpl ▸ Settings: General, Notifications, Grades, Data, Updates, About
    Shell/ToolWindow.swift      an external tool (or a page Simpl does not draw) in a window of its own
    Shell/WhatsNewSheet.swift   what changed, after an update
    Screens/                    the native screens (Dashboard, Courses, To Do, Calendar, Grades, Notifications, Inbox,
                                Groups, Search, a course's home and sections, an assignment, a discussion, a quiz, setup)
    Components/                 cards, rows, rings, chips, the loader, Canvas's rich text
    Support/Platform.swift      the theme, the house springs, what the shared code expects of the app
    Support/Shot.swift          the screenshot suite's camera (does nothing without its launch argument)
    Support/Updater.swift       updates: the feed, the download and its checks, the swap and relaunch, earlier versions
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
- **Motion.** Springs only, critically damped (`Motion.snappy`, `gentle`, `hover`, `fill`): they start from what is on
  screen, so anything can be interrupted and turned around. Cards lift under the pointer and give under a click; a place
  cross-fades in; a push slides as Apple's stacks do; a tick shows before its row moves. Under Reduce Motion, moves become
  cross-fades.
- **Keyboard.** ⌘1–6 the places, ⌘[ ⌘] Back and Forward, ⌘N a task, ⇧⌘N a message, ⌘R reload, ⌘, Settings; arrows and
  Return in lists and pickers; ⌘Return sends.

- **Knewton Alta as a quiz (1.3.17, Mac only).** Open Tool on a Knewton Alta assignment (the engine's `alta`: its launch
  address, or a title naming it) opens a popup as a quiz does (`Screens/AltaQuiz.swift`): the objectives down the left
  as the quiz's tiles (the one being worked on filled, a mastered one ticked, the rest filling as they are mastered),
  each by name with its own bar, and the total mastery bar; in the rest, the question drawn by the app — its maths
  typeset with the KaTeX the app carries, a multiple choice as the quiz's option rows, blanks as fields, drop-downs as
  menus, a written answer — with Check, Alta's verdict and its words, and Continue. Alta still sets and marks every
  question: `Engine/AltaSession.swift` keeps Alta open, signed in through Canvas, on a page out of sight; a script in it
  (`AltaHook`, Alta's hosts only) reads a copy of Alta's own `content` answer — never the answer key or who the student
  is — puts the student's answer into Alta's question as a click or typing would, presses Alta's Check and Continue, and
  reads the verdict back. A question the app does not draw (a Desmos graph, a formula, a lesson) shows Alta's own page
  in its place, as does Alta's Page in the top bar. (1.3.18) Alta opens on the assignment's overview: the hook reads
  whatever Alta's page reads there (the assignment, its objectives with their estimated questions, found by those fields)
  and the popup shows a start screen — status, due date, the objectives, and Start or Continue, which presses Alta's
  own — in place of Alta's page; the rail fills from it before the first question. (1.3.19) A school's Alta can draw
  its overview without an answer naming the objectives, and its START as a link in a web component: the script runs in
  every page of the hidden view (Alta's site names are not guessed), finds buttons by their words wherever they are
  (shadow trees included), and reads the overview from the page's own words when it must (the title, DUE DATE, STATUS,
  each "Estimated 4 - 9 questions" and the objective under it). *Copy Alta Details* (the popup's More menu) copies what
  it saw — page addresses without queries, the field names of Alta's answers (never values), the buttons' words — for a
  school whose Alta still differs. (1.3.20) Maths answers (Learnosity's formula fields, `clozeformula`, `formulaV2`) are
  drawn too: the template's words typeset round a field the student types in as on a keyboard (/, ^, sqrt), put into
  Alta's field through MathQuill (its API when the page offers it, else its own text box). Alta's welcome pop-ups are put
  away by their own Got it (only inside a dialog). Objective names missing from Alta's answers come from the overview, by
  place. (1.3.21, a beta) Any other kind of question (a graph, a drag and drop, any widget) is Alta's own, shown alone:
  the page script hides (inline `display: none`, put back exactly) everything off the paths from Learnosity's question
  element, Check/Next/help buttons, dialogs and feedback up to the body, worked out again whenever the page changes; a
  question inside an inner frame leaves its outer page holding only that frame. *Alta's Page* shows the page whole.
  Alta's own Check pressed there is heard too. The rail's mastery takes the newest word from any of Alta's answers (a
  checked answer's `statusAndProgress`, not only `content`'s) and from Alta's own progress bars and "Mastery N%". The
  camera button (and More ▸ Take Full-Page Screenshot) scrolls Alta's whole page a screenful at a time, stitches it into
  one PNG in Downloads and copies it. (1.3.22) Shown alone, the question keeps the smallest part of the page holding
  Learnosity's element, Alta's Check/Next (greyed out or not) and the words it asks (found by a run of the prompt's
  plain text); a question is drawn by its Learnosity type unless Alta calls it a lesson, video or example. Copy Alta
  Details adds the question's type and how it was drawn, the rail, and the page's elements round the question (class
  names only). (1.3.23) Every question is answered in Alta's own answer box (Learnosity's maths field, choices, blanks,
  graphs), shown alone and recoloured by `AltaTheme` (light or dark as the app is, its accent; a weightless `:where()`
  base so the answer box's rules win; the web view draws no ground of its own). Alta's Check and Next are moved aside
  (still pressed, programmatically) and the popup's bar presses them; the page script says when the question is found
  and when its Check is ready. If Alta's box is not found in 6 s, the popup draws the question itself as before. The
  overview's objectives (names, "Estimated N - M questions") are kept per assignment and name the rail's objectives,
  lined up by the one on screen, and give its estimate. (1.3.24) The trim is worked out again only for changes outside
  what is kept (typing, a maths field's blinking cursor no longer re-trim it, which threw the page back to its top), and
  holds every scrolled ancestor's position through it; Alta's maths keypad (`[class*=keyboard|keypad]`) is left out of
  the theme, only inverted in dark mode (1.3.24 was not released; it went out in 1.3.25). (1.3.25) Alta's Feedback
  button is put away and its More Instruction moved aside, pressed from the popup's bar beside Check; the bar sits in the
  bottom right corner. (1.3.26) Alta's pop-ups (More Instruction's Having trouble?) take the app's page colour as
  their ground (resolved light or dark at the time), so the question no longer shows through; the bar is put away while
  one is open. (1.3.28; 1.3.27 went out in it) Alta's lessons (More Instruction ▸ View Instruction) are shown alone like
  a question — the smallest part holding the lesson's words and its Continue, else the nearest part round Continue with
  something to read — and the bar offers Continue; any of Alta's buttons the bar stands for still in sight brings a
  fresh trim. (1.3.29) Typeset maths (MathJax 2's `MathJax_CHTML`/`mjx-*`, MathJax 3, KaTeX, MathQuill) is left
  wholly out of the theme's base rule — its own fonts kept, only the text's colour — and maths still in raw TeX in what is
  shown (not MathQuill's hidden copy, MathJax's source or KaTeX's) is covered with "Setting out the maths…" for up to
  10 s. The bar offers Continue whenever Alta's Continue can be pressed and its Check cannot. (1.3.30) The trim never
  hides a typesetter's own helpers (MathJax 2's `MathJax_Hidden`, `MathJax_Font_Test`: by id or class) or anything
  already invisible — hidden, MathJax could not measure its fonts, waited long and set the maths out wrong; raw maths
  counts MathJax's preview and a busy MathJax queue; the lesson's Continue shows whenever it is on the page, greyed out
  until Alta's is ready; Copy Alta Details marks greyed-out buttons. (1.3.31) After a lesson's Continue, Alta puts the
  question under the lesson on the same page: the popup goes on as soon as the question is there (no wait for a new
  question from Alta's answers), keeps the question with its own card ("Question…", when its words were not found)
  and hides the lesson; the bar's View Instruction shows the lesson again in its place and Back to Question the
  question. Hidden parts are put back only where the page has not changed them since. (1.3.33) The question's own card
  (beginning "Question", long with its maths) is never taken for the lesson above it. (1.3.34) Alta may send a lesson
  and its question in one answer, the lesson first: the question is taken as the first state that is a question (not
  a lesson, video or example), from any answer of Alta's carrying states; an answer without the objectives keeps the
  last ones. Copy Alta Details lists each answer's states by purpose / dataType / question type (nothing in them).
  (1.3.35) Alta keeps the question on the page under a lesson: a lesson newly come above it is shown in its place by
  the page script itself (it flashed, then the question was kept again), the view passed to the app; the bar has the
  lesson's Continue and Back to Question. Done with a lesson (its Continue, Back to Question), what is left above in the
  next 10 s is noted, not shown. The app sends a view only when the student picks one.
- **Quizzes (1.3.32).** The quiz's start screen lists every finished attempt, newest first, with its score (when Canvas
  shows results) and when it was handed in, the one Canvas keeps by the quiz's scoring rule marked Kept (`quizIntro`'s
  `attempts`, from the quiz assignment's `submission_history`). A quiz's card preview reads the quiz's own assignment
  (`quizAssignment`), so a graded quiz shows its score wherever its card is, not only "Graded".
  `scripts/dev/alta-test.mjs` drives the mock's Alta player
  (`/mock-alta/…`) through it in Chromium; the popup is shot as `21a-alta`.

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
[:take:<n>|:review] | tool:<course>:<id> | settings | setup | newtask | compose | whatsnew | handin`,
`-SimplAppearance light|dark`, `-SimplSettingsTab notifications|grades|data|updates|about` (the tab Settings opens on).

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
