# Brightspace (D2L)

Since 2.99.22 the interface also runs on Brightspace. It was written against Canvas — every screen, the store, the apps'
native screens through `native-app.js` — and it still speaks only Canvas's language. On a Brightspace page, Brightspace is
made to answer in it: the screens never know the difference.

## How it fits

```
extension/lib/lms.js       which platform a page belongs to, and the addresses either way (loaded first, document_start)
extension/lib/d2l-api.js   Brightspace's API (Valence) answering the interface's Canvas requests in Canvas's shapes
extension/lib/canvas-api.js  the one door to Canvas: on a Brightspace page its requests go to lib/d2l-api.js instead
scripts/dev/mock-brightspace.mjs   a made-up student's Brightspace, for the tests
scripts/dev/d2l-test.mjs   the layer in a sandbox against the mock, then the extension in Chromium on it (test-all: d2l)
```

**Which platform.** `lib/lms.js` sets `BCV.lms` before anything else runs: `kind` (`'d2l'` or `'canvas'`), `d2l`,
`name` (`'Brightspace'` or `'Canvas'`, for the words that name it). A page is Brightspace's on its own domains
(`*.brightspace.com`, `*.d2l.com`, `*.desire2learn.com`), or anywhere under `/d2l/` (a school's own address,
`d2l.school.edu`), and a site seen once is remembered (`localStorage['bcv:lms']`) for its pages outside `/d2l/`. The
page is marked `html.bcv-d2l`.

**Addresses.** The interface's screens keep their Canvas addresses (`/courses/31001/assignments/702`); the address bar
shows a Brightspace page that always loads, for whoever is enrolled, whatever the school's Brightspace looks like:

| The interface's | Brightspace's |
| --- | --- |
| `/` (the Dashboard), `/#todo` … | `/d2l/home`, `/d2l/home#todo` |
| `/courses/<ou>` | `/d2l/home/<ou>` |
| any other screen it draws | `/d2l/home[/<ou>]?simpl=<the interface's address>` |
| a quiz (`/courses/<ou>/quizzes/<id>`, or its assignment) | Brightspace's own quiz page, left as it is |
| a content topic it does not draw, a file opened elsewhere | Brightspace's content viewer, left as it is |

Brightspace's own pages for things the interface draws (a dropbox folder, the grades, the content, a discussion, the
news, the classlist, the calendar) are read back as those screens (`fromPage`). A page it has no screen for
(preferences, a tool of Brightspace's own) is left to Brightspace. `?bcv=native` ("Open in Brightspace") asks for
Brightspace's own page for a screen, and carries the flag there so the interface leaves it be.

The interface edits its own query (`?bcv=setup`, `?bcv=whatsnew`…) through `BCV.lms.routeUrl()` and `pageFor()`, since
on Brightspace that query rides inside `?simpl=`. Its links keep their Canvas addresses — its router takes a plain click
to the Brightspace page — and are pointed at the Brightspace page for any other way out (a middle or ⌘-click, a
`target=_blank` link, the link's menu, a drag, its own `window.open`): Brightspace answers an address outside `/d2l/`
with a bare 404 that does not say what was asked for. One that reached its 404 with the address named (`targetUrl`) is
sent on at once.

**The API.** `lib/d2l-api.js` reads Valence at `lp` 1.50 and `le` 1.82 (or the newest the school has, if not those):

| Canvas | Brightspace |
| --- | --- |
| courses, terms, favourites | course-offering enrollments (both paged shapes), the course's semester, pins |
| a course's total | the calculated final grade |
| assignments | dropbox folders; quizzes (id 1e9 + id); graded discussion topics (2e9 + id); grade items no tool owns (4e9 + id) |
| submissions | the student's dropbox submissions and grade values (a graded quiz counts as taken) |
| assignment groups | grade categories and their weights (weighted when the grading system is) |
| announcements | news items (3e9 + id) |
| discussions | forums' topics and their posts, threaded; replies posted with the page's token |
| modules | the content's table of contents: a module inside one is a heading, its topics a step in |
| pages, files | HTML topics (read with the session), file topics (fetched from the topic; previewed in Brightspace's viewer) |
| people | the classlist |
| the calendar | the course calendars' events (a due date's own entry left out: its work is listed already) |
| a hand-in | one `multipart/mixed` post to the folder — the comment, then each file (text as a page of its own) |

Writes carry `X-Csrf-Token` (from `/d2l/lp/auth/xsrf-tokens`), and a write refused for it is tried once with a fresh one.
What Brightspace keeps nothing of — a course's nickname and colour, the student's own tasks, a tick or a dismissal in
To Do, which announcements were read — is kept in the extension's storage per site (`d2l:<host>`) and answered from
there. What Brightspace has no such thing for (the Inbox, groups, appointments, the activity stream) answers empty, and
the sidebar has no Inbox or Groups.

## Builds

`*.brightspace.com` is built in like `*.instructure.com` (`host_permissions`, the interface's content scripts, the
sniffer's `exclude_matches`). A school's own Brightspace is found by `content/sniff.js` — a page under `/d2l/` that names
who is signed in on `<html data-global-context>`, or Brightspace's sign-in page — and enabled as a school's own Canvas is.

The quiet Chrome build (`scripts/chrome-manifest.py --no-sniffer`) does **not** name Brightspace's domain: a host it did
not name before is a new warning at an update, and Chrome switches every installed copy off until it is accepted. A
Brightspace site is enabled there from the toolbar button, as any school's own site is; its scripts are the interface's
own, the Brightspace layer among them. `scripts/dev/chrome-setup-test.mjs` holds both builds' lists exactly.

## Testing

`node scripts/dev/d2l-test.mjs` (in `test-all.mjs` as `d2l`) runs against `scripts/dev/mock-brightspace.mjs`, a made-up
student with grades given and withheld, work handed in, late and missing, a weighted course, a discussion with replies
and content with a module inside a module. Every write the mock takes is listed at `GET /__mock/log`; each must carry
the token.

On a real Brightspace (a trial is free from D2L), sign in with a form post to `/d2l/lp/auth/login/login.d2l`
(`userName`, `password`, `loginPath=/d2l/login`) and load the extension as the suites do. Keep the credentials in the
environment, never in a file. An instructor's account sees no grades or submissions of its own: a student's shows
the rest.

## Not yet

- The iPhone and Mac apps load the same scripts, but their own sign-in handling and address routing are Canvas's.
- Quizzes are taken on Brightspace's own pages; the interface does not draw them.
- Brightspace gives a student no question count for a quiz and no list of quiz attempts.
