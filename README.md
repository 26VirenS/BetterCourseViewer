# BetterCourseViewer

A Mac app that installs a Safari extension giving **Canvas** a new interface, drawn to a single design: rounded cards on a soft grey ground, one sidebar, segmented controls, an iOS-style dark appearance, and a discreet **smart panel** that reads the page you are on using your own Claude or ChatGPT key.

Nothing is decorated; every screen is redrawn from the Canvas API with your own data. A switch at the top-left of every page turns the new look off and on.

| Dashboard (list view) | Course grades with nested rings |
| --- | --- |
| ![Dashboard](docs/screenshots/dashboard.png) | ![Grades](docs/screenshots/grades.png) |

| Calendar (month) | Course home |
| --- | --- |
| ![Calendar](docs/screenshots/calendar.png) | ![Course home](docs/screenshots/course-home.png) |

| Dark appearance | Smart panel on an assignment |
| --- | --- |
| ![Dark](docs/screenshots/dark.png) | ![Smart panel](docs/screenshots/smart-panel.png) |

(Taken against the bundled mock Canvas, so the course content is fake.)

## What you get

**Sidebar** – site name and term, Dashboard / Courses / Groups / To Do (with a count) / Calendar / Inbox (with unread count), your favourite courses with their Canvas colours, a Dark/Light appearance switch and your account.

**Dashboard** – three counters (due today with points, due this week across N courses, unread announcements), a per-course workload bar for the week (submitted ÷ assigned), and the same three views Canvas has: **Cards** (with a submitted-items progress bar, quick links and a "2 due today" badge), **List** (by day, with a circle to mark items done) and **Recent activity**. The view you pick is saved to your Canvas profile, as Canvas does.

**Courses** – favourites as cards with term, role, progress and what is due next; every other course grouped by term with a star to add it to your dashboard; All / Past / Future and search.

**To Do** – everything from today through the next seven days, by date or by course. It knows what is *actually* due: assignments, quizzes and graded discussions carry a due date; pages, events and ungraded discussions with only a to-do date are labelled as such. Dismiss hides an item without touching the work.

**Calendar** – Week, Month and Agenda views over your Canvas calendars, an agenda **range picker** (tap a start and an end day), calendar switches (Canvas's ten-calendar limit is enforced with a message), and struck-through text for submitted or past items.

**Inbox** – conversations with course and scope filters, a reader with reply, and compose with recipient search. Stars, read state and sending all go through the Canvas API.

**Groups** – current and previous groups.

**Courses** – a header with the course colour, term and an **Immersive Reader** button (a clean large-type reading view of the page), tab pills straight from the course's own navigation, and:
- **Home** – the front page (or modules / syllabus / assignments / stream, whichever the instructor chose) with link chips, plus course links and the course To Do.
- **Announcements**, **Assignments** (by date or by type, with status badges), **Discussions** (unread and reply counts), **People** (roles, sections, pronouns), **Pages**, **Files** (folders, selection, download), **Quizzes**, **Modules** (requirements and completion).
- **Grades** – nested rings: the outer ring is your total as Canvas reports it; one inner ring per assignment group that has graded work; groups with nothing graded are listed instead of drawn. Weights, "not counted" and late badges, and a **what-if mode**: edit any score to see the outcome. Nothing is saved or sent anywhere.
- Item views for assignments (rubric, submission, comments), discussion threads (nested replies, reply box), pages, quizzes and the syllabus.

Anything without a screen of its own – taking a quiz, submitting, external tools, file previews, profile pages – is shown as Canvas drew it, inside the same shell, with a link back to stock Canvas.

**Smart panel** – the small button at the bottom-right. It reads the current page and suggests actions that fit it: summarize what's due, plan the week, summarize an assignment or make a checklist, draft a discussion reply, condense a page, explain rubric feedback or a grade. Replies stream in; drafts can be copied or inserted into the reply box for you to edit. The word "AI" never appears; it is the smart panel.

## Requirements

- macOS 13 Ventura or later, Safari 16.4 or later
- Xcode 15 or later (free, from the Mac App Store) to build the Mac app
- Optional: a [Claude API key](https://console.anthropic.com/settings/keys) and/or a [ChatGPT API key](https://platform.openai.com/api-keys) for the smart panel

## Install (build the Mac app)

Safari extensions ship inside a Mac app, so the app is built with Xcode. The script uses Apple's converter to generate the Xcode project from the `extension/` folder.

1. Clone the repo and open a Terminal in it.
2. Generate the Xcode project and open it:
   ```bash
   ./scripts/build-mac-app.sh --open
   ```
3. In Xcode press **⌘R** (Product → Run). The BetterCourseViewer app opens and tells you the extension is ready.
4. In Safari go to **Settings → Extensions**, tick **BetterCourseViewer**, and click **Always Allow on Every Website** (or allow it on your school's Canvas site when Safari asks).
5. If you built without an Apple developer team, turn on **Safari → Settings → Developer → Allow unsigned extensions** (enable the Developer tab under Settings → Advanced if it is hidden).
6. Click the BetterCourseViewer toolbar icon → **Settings**, and paste your Claude and/or ChatGPT key. Press **Test** to check it.

Updating: `git pull`, then in Xcode Product → Clean Build Folder (⇧⌘K) and Product → Run (⌘R), then quit and reopen Safari. The project references the files in `extension/` directly, so a rebuild is all that is needed. If Safari still shows stock Canvas, delete the `macos/` folder and regenerate the project from scratch.

To build from the command line instead of Xcode:

```bash
./scripts/build-mac-app.sh --build     # produces macos/build/BetterCourseViewer.app
```

### Build errors

- **"Embedded binary's bundle identifier is not prefixed with the parent app's bundle identifier"** – the two targets' identifiers drifted apart. Select the project, then the **BetterCourseViewer** target → Signing & Capabilities and note its Bundle Identifier; then select the **BetterCourseViewer Extension** target and set its Bundle Identifier to that value plus `.Extension`. Give both targets the same Team, then Product → Clean Build Folder and run again. `rm -rf macos && ./scripts/build-mac-app.sh --open` also fixes it.
- **"Failed to register bundle identifier"** (personal/free teams) – pick your own: `BUNDLE_ID=com.yourname.bettercourseviewer ./scripts/build-mac-app.sh --open` (after deleting `macos/`).

### School with its own Canvas address?

The extension is on automatically for every `*.instructure.com` site. If your school uses a custom address such as `catcourses.ucmerced.edu`:

1. Open that site in Safari.
2. Click the BetterCourseViewer toolbar icon → **Enable on catcourses.ucmerced.edu**, or add it under **Settings → Canvas sites**.

## Turning the look off

- The **Skin** switch at the top-left of every Canvas page turns the new interface off; a small switch stays there so you can turn it back on.
- The toolbar popup and Settings → Appearance have the same switch, plus Light / Dark / Match the system.

## The smart panel and your keys

- Add one or both keys under **Settings → Smart panel**. If both are set, the one you mark as preferred is used (Claude by default); otherwise whichever key exists is used.
- Default models are `claude-opus-5` and `gpt-5`; both are editable. **Response depth** trades speed and cost for more thorough answers.
- Keys are stored only in the extension's local storage on your Mac and are never included in settings exports.
- Requests go straight from your browser to `api.anthropic.com` or `api.openai.com` with your key and include only the page you are reading (this can be turned off under Settings).
- The Claude integration turns on Anthropic's server-side refusal fallbacks, so a request the safety classifier declines is retried on Anthropic's recommended substitute model automatically.

## How it stays honest

- No control does anything the Canvas student API cannot do: marking done and dismissing are planner overrides, stars are favourites, replies and messages go through the same endpoints Canvas uses, and the dashboard view is stored on your Canvas profile.
- Every number on screen comes from an API response. The grade rings use your assignment groups and submissions; the total is the score Canvas reports until you enter what-if values. If a request fails the card is hidden rather than showing a false zero.
- Course colours, nicknames, favourites and the dashboard view all come from your Canvas settings.

## Other browsers

The `extension/` folder is a standard Manifest V3 web extension and also loads in Chrome, Edge and Firefox:

- Chrome/Edge: `chrome://extensions` → Developer mode → **Load unpacked** → choose `extension/`.
- Firefox: `about:debugging` → **Load Temporary Add-on** → choose `extension/manifest.json`.
- `./scripts/package.sh` produces `dist/bettercourseviewer-<version>.zip`.

## Development

```
extension/
  manifest.json                Manifest V3 (Safari, Chrome, Firefox)
  background.js                streaming proxy to Claude/ChatGPT, key tests, custom sites
  lib/                         settings, providers (raw fetch + SSE), Canvas REST helper, markdown, utils
  content/early.js             document_start: applies skin + appearance before first paint
  content/styles/app.css       the whole design system (light/dark variables, every component)
  content/app/icons.js         icon paths
  content/app/ui.js            components, date formatting, colour math
  content/app/store.js         every Canvas API loader + the grade model
  content/app/app.js           shell (sidebar), router, skin switch, hybrid pages
  content/app/smart.js         the smart panel
  content/app/screens/         dashboard, courses, todo, groups, calendar, inbox,
                               course (shell + tabs), course-detail, grades, native
  popup/, options/             toolbar popup and the settings page
scripts/
  build-mac-app.sh             generates the Xcode project / builds the .app
  package.sh                   zips the extension
  make-icons.mjs               regenerates the PNG icons
  dev/mock-canvas.mjs          a fake Canvas (pages + the API endpoints the app reads)
  dev/smoke-test.mjs           walks every screen in headless Chromium against the mock
```

The generated Xcode project copies the extension's top-level folders (`content/`, `lib/`, `popup/`, `options/`, `icons/`) into the app as folder references, so new files inside them are picked up by the next build. A **new top-level folder** is not: keep new code under an existing folder, or delete `macos/` and regenerate the project.

```bash
node scripts/dev/mock-canvas.mjs        # http://localhost:8787
node scripts/dev/smoke-test.mjs         # screenshots in scripts/dev/out/
```

## Privacy

- No analytics, no accounts, no servers of its own.
- Canvas requests go to your Canvas site with your existing session cookie and are cached briefly on your Mac.
- Smart requests go directly from your browser to the provider with your key.

## License

MIT
