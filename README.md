# BetterCourseViewer

A Mac app that installs a Safari extension giving **Canvas** a new interface, drawn to a single design: rounded cards on a soft grey ground, one sidebar, segmented controls, an iOS-style dark appearance, and a discreet **smart panel** that reads the page you are on using your own Claude or ChatGPT key.

Nothing is decorated; every screen is redrawn from the Canvas API with your own data, on top of the real Canvas page for that URL. The look is switched off and on from the toolbar popup or the settings, and turning it off always reveals exactly the page you were on.

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

**Sidebar** – your school's own mark from Canvas's theme (its app icon, favicon or header logo; Canvas's default assets never count, and Settings can point at another image), the site name and term, Dashboard / Courses / Groups / To Do (with a count) / Calendar / Inbox (with unread count) / Grades, your favourite courses with their Canvas colours, a Dark/Light appearance switch and your account.

**Dashboard** – three counters (due today with points, due this week across N courses, unread announcements), a per-course workload bar for the week (submitted ÷ assigned; courses with nothing assigned fold away under a disclosure), and the same three views Canvas has: **Cards** (with a submitted-items progress bar, quick links and a "2 due today" badge), **List** (by day, with a circle to mark items done) and **Recent activity**. The view you pick is saved to your Canvas profile, as Canvas does.

**Courses** – your dashboard courses as cards with term, role, progress and what is due next; every other course grouped by term (groups can be reordered with the arrows, and the order is remembered) with a star to add it to your dashboard; All / Past / Future and search.

The three counters at the top of the dashboard (due today, due this week, unread announcements) each open a sheet listing exactly the items they counted, with points, times and the course, so a number is never a dead end.

**To Do** – everything from today through the next seven days, by date or by course. It knows what is *actually* due: assignments, quizzes and graded discussions carry a due date; pages, events and ungraded discussions with only a to-do date are labelled as such. Tick an item to mark it done in your Canvas planner (and untick it later); dismiss hides it without touching the work; a switch shows completed and dismissed items so they can be restored.

**Calendar** – Week, Month and Agenda views over your Canvas calendars, an agenda **range picker** (tap a start and an end day), calendar switches (Canvas's ten-calendar limit is enforced with a message), and struck-through text for submitted or past items. Canvas refuses a whole calendar request when one course is off-limits, so a refused calendar is retried alone, marked "Not shared" and the rest still load; if the calendar API fails altogether, the planner's items fill in with a note saying so.

**Inbox** – conversations with course and scope filters, a reader with reply, and compose with recipient search. Stars, read state and sending all go through the Canvas API.

**Groups** – current and previous groups, each with its own screen: activity, announcements, discussions, pages, people and files.

**Grades** – one page for the whole term. A **term GPA** card turns every current course's Canvas score into a letter and its 4.0 points (A 4.0, A− 3.7 … the usual 93/90/87 cut-offs) and averages them with every course counting equally; underneath, the range your GPA lands in if all ungraded work comes in ten points lower, and how far you are from your goal. Canvas stores no GPA and no history, so **tracking** is opt-in: enter the GPA you had before this term and how many courses it covers, and the page shows a *cumulative* figure and saves one snapshot a day from then on (nothing is back-filled), drawing a **trend** line against a dashed goal line once it has two. Three stat cards: **Momentum** (this snapshot against the previous one), **On-time submissions** (submitted before the due time, from the `late` flag on your submissions) and **Grade mix** (a chip per course letter, highest and lowest named). Then **one card per course**: a ring with the score, the letter and its points, and a bar with your target marked on it. Hovering the ring alone opens it into one thin ring per assignment group with a mini breakdown, so five courses can be compared without opening any of them; **Details** opens the course's own grade page in a sheet (nested rings, per-group percent and weight, the 100%-weight strip with dashed segments for groups that have nothing graded yet, the assignment list) and holds the −/+ **target grade**: the card says what percentage of the still-unscored points you need to reach it, when it is already secured and when it is out of reach, using the same group weights as the course's own grade card. The eye button **hides** a course from the overview and the GPA maths (an audit, a P/NP course); it waits in a tray marked *not counted in GPA* until you show it again, and the header always says "N courses · M with grades so far". A course Canvas has not scored shows **N/A**, contributes nothing and gets no target. The gear opens the GPA settings sheet (goal in 0.05 steps, the tracking switch and its two inputs); *Reset setup* at the bottom forgets the prior record and the snapshots. The GPA is a reading of your Canvas scores, not the figure on your transcript, and the page says so.

**Courses** – a header with the course colour, term and an **Immersive Reader** button (a clean large-type reading view of the page), a course rail built from the course's own navigation and grouped as *Course* / *Materials* / *People* / *Campus tools* (external tools become plain links), with counts for unread announcements and grades posted this week, the active item in the course colour, and a Collapse control that shrinks it to tiles. Then:
- **Home** – the front page (or modules / syllabus / assignments / stream, whichever the instructor chose) with link chips, plus course links and the course To Do.
- **Announcements**, **Assignments** (by date or by type, with status badges), **Discussions** (unread and reply counts), **People** (roles, sections, pronouns), **Pages**, **Files** (folders, selection, download), **Quizzes**, **Modules** (requirements and completion).
- **Grades** – one card that reads the whole grade at a glance: nested rings (the outer ring is your total as Canvas reports it, one inner ring per assignment group that has graded work, 0%-weight groups drawn stippled) with the total beside them; a *By group* legend with points, weight and the items Canvas excludes; groups with nothing graded listed without a ring; and a *How the grade is weighted* bar that shows each weighted group's share, filled once it has a score. Then the assignment list with "not counted" and late badges, and a **what-if mode**: edit any score to see the outcome. Nothing is saved or sent anywhere.
- Item views for assignments (rubric, submission, comments), discussion threads (nested replies, reply box), pages, quizzes and the syllabus.
- **Handing work in** – "Submit assignment" on an assignment (or **Submit** on a To Do row) opens a one-column flow: the assignment's own rules up top (due, points, attempt *N of M*, availability window, what it accepts), the instructions, then **File upload** / **Text entry** / **Other**. The drop zone's helper line and the picker's accepted types come from the assignment's `allowed_extensions`, and a file of the wrong type is refused on the spot with the reason instead of after a long upload. Files go through Canvas's own three-step upload with a progress bar per row; the text entry is kept as a draft on this Mac until it is sent; **Other** lists exactly the tools the instructor enabled for handing work in (Box, Office 365, …) plus *Website URL*, and a tool row punches through to the tool's own picker in a sheet, taking the file it hands back into the attachment list. An optional comment goes with the submission. The footer says when late starts; after **Submit assignment** a receipt shows when it landed, what was sent, how long before (or after) the deadline, the attempt and the grade state, with **Resubmit** when attempts remain. Media recordings and annotations stay on Canvas's own page.
- **Taking a quiz** – "Take the quiz" opens a focused flow with the chrome hidden: an intro card with the quiz's own rules (time limit, attempts, one-question-at-a-time, no going back, access code), then the questions either one at a time or all on one scrolling page, progress pills that jump between questions, flag for review, a timer pill that counts down from the attempt's end time (with warnings at five and one minute), a review screen listing every answer before **Submit quiz**, and a submitted screen with the score when Canvas releases it. Every answer and flag is sent to the Canvas quiz-submission API as you go; nothing is ever submitted for you. Question types Canvas only renders itself (matching, fill-in-the-blanks, file upload…) hand off to Canvas's own quiz page on the same attempt.

Anything without a screen of its own – external tools and their embeds (Box, YuJa…), file previews, profile and settings pages, Canvas's own quiz page – is a **punch-through**: Canvas's page is left exactly where it is in the DOM, so tool launches and embeds keep working, and the new sidebar, header and course rail float over it with a hole that Canvas's content is laid out into (its own right column, such as a quiz's question list and timer, stays beside it). In the dark appearance the hole is darkened with a filter; a **View in light mode** button at the top right of the punch-through area shows the page as Canvas drew it instead (some embeds read better that way), and the choice is remembered per site. While a quiz attempt is open (ours or Canvas's), navigation is hidden and leaving asks for confirmation.

**Motion and loading** – screens rise in, sheets rise over a fading scrim, popovers pop; state changes (a ring opening, a bar filling) apply instantly, and transitions are kept for hover only. While a page loads, a thin bar sweeps along the top of the window, and skeleton blocks shaped like the content (list rows on the Dashboard, course cards on Grades) hold its place so nothing jumps when the data lands; a page that comes from cache never flashes either. Under *Reduce Motion* the entrances are dropped and the loading indicators stay.

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

- The toolbar popup and Settings → Appearance have the **BetterCourseViewer look** switch, plus Light / Dark / Match the system. Nothing sits on the page itself.
- Pages Canvas draws itself have an **Open in stock Canvas** button that flips the same switch.

## The smart panel and your keys

- Add one or both keys under **Settings → Smart panel**. If both are set, the one you mark as preferred is used (Claude by default); otherwise whichever key exists is used.
- Default models are `claude-opus-5` and `gpt-5`; both are editable. **Response depth** trades speed and cost for more thorough answers.
- Keys are stored only in the extension's local storage on your Mac and are never included in settings exports.
- Requests go straight from your browser to `api.anthropic.com` or `api.openai.com` with your key and include only the page you are reading (this can be turned off under Settings).
- The Claude integration turns on Anthropic's server-side refusal fallbacks, so a request the safety classifier declines is retried on Anthropic's recommended substitute model automatically.

## How it stays honest

- No control does anything the Canvas student API cannot do: marking done and dismissing are planner overrides, stars are favourites, replies and messages go through the same endpoints Canvas uses, and the dashboard view is stored on your Canvas profile. Handing work in is the same POST Canvas's own form sends, after the same file-upload steps; the submission tools are never re-implemented, only framed.
- Every number on screen comes from an API response. The grade rings use your assignment groups and submissions; the total is the score Canvas reports until you enter what-if values. If a request fails the card is hidden rather than showing a false zero.
- Course colours, nicknames, favourites and the dashboard view all come from your Canvas settings.
- The Grades page computes its GPA from the scores Canvas returns for your current courses on a plain 4.0 scale with every course weighted equally, because Canvas exposes neither a GPA nor credit hours; it is labelled as computed, not official. Your prior GPA, course count, goal, target grades and the daily snapshots are stored only in the extension on this Mac and never sent to Canvas or anywhere else.

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
  content/app/app.js           shell (sidebar), router, punch-through for Canvas-drawn pages
  content/app/smart.js         the smart panel
  content/app/screens/         dashboard, courses, todo, groups, calendar, inbox, gpa (the Grades page),
                               course (shell + tabs), course-detail, grades (course grade card), quiz, submit, native
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
