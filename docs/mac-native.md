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
  atoms added to the selected one with a chosen bond order, hydrogen filled in, presets; shaped by forces (bond springs,
  each atom's bonds and lone pairs — points of their own — pushing apart, lone pairs harder, non-bonded atoms kept
  apart), so VSEPR shapes come out; SceneKit with camera control and click-to-select; ball and stick, space filling or
  sticks; lone pairs, labels, turning by itself; its formula (Hill order), molar mass, counts, valence check, polarity
  (from bond dipoles and lone pairs) and the selected atom's shape and bond angles as the model has them.
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
