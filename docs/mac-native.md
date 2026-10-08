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
    Shell/SettingsView.swift    Simpl ▸ Settings: General, Notifications, Grades, Data, About
    Shell/ToolWindow.swift      an external tool (or a page Simpl does not draw) in a window of its own
    Shell/WhatsNewSheet.swift   what changed, after an update
    Screens/                    the native screens (Dashboard, Courses, To Do, Calendar, Grades, Notifications, Inbox,
                                Groups, Search, a course's home and sections, an assignment, a discussion, a quiz, setup)
    Components/                 cards, rows, rings, chips, the loader, Canvas's rich text
    Support/Platform.swift      the theme, the house springs, what the shared code expects of the app
    Support/Shot.swift          the screenshot suite's camera (does nothing without its launch argument)
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
`-SimplAppearance light|dark`.

## Releasing

Simpl for Mac has its own version line (`MARKETING_VERSION` and `BUILD` in `scripts/dev/make-mac-project.py`; 1.0 was
the first). A release is made by hand: **Actions → Simpl for Mac release → Run workflow**
(`.github/workflows/mac-native-release.yml`, `scripts/release-mac-native.sh`). It builds the app for Apple silicon and
Intel, signs it with the team's Developer ID (the same five secrets the Safari app's release uses, `docs/mac-app.md`),
has Apple notarize it, staples the ticket and publishes `Simpl-Mac-<version>.zip` as a GitHub Release of its own,
`mac-v<version>`. A version already released is refused, never replaced: raise the version (and the build) first and
run `python3 scripts/dev/make-mac-project.py`. The site's Mac download and update feed stay the Safari app's.

`simpl://` links open in the app: `simpl://courses/101/assignments/1001` opens that assignment.

## CI

`.github/workflows/mac.yml` builds the app with Xcode 26 on every change to `mac/` or the shared files, runs it against
the mock Canvas, and pictures every screen in light and dark (`scripts/dev/mac-shots.sh`). The pictures, the build's
errors and warnings and the built app go to the `mac-shots` branch. Nothing is signed for distribution or published.
