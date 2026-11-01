# Error codes

Every error Simpl Courses shows — a toast at the bottom of the page, or a block on a screen that
could not be loaded — carries a short code after its words, in a small grey tag. The code says
where it happened and what went wrong, so a report can say it exactly. The **Report a bug** button
(the purple disc left of the switch at the top right) carries the codes Simpl showed lately to
[simplcourses.com/report](https://simplcourses.com/report/), where they are filled in.

```
SC-<screen>-<what>        e.g.  SC-C-404   SC-D-NET   SC-Q-403   SC-W-LOAD
```

Nothing about a code leaves the browser by itself. The codes are kept for a report: the page's
last dozen in memory, and the last day's twenty in the extension's own storage (`errors:recent`),
so a report sent from another page still has them. The report page takes the last hour's.

## Screen

| Letter | Where |
| --- | --- |
| D | Dashboard (and any page of the new look not listed below) |
| T | To Do |
| K | Calendar |
| I | Inbox |
| N | Notifications |
| G | Grades (the overview and the GPA) |
| W | Tools |
| C | Courses, and a course's own pages (home, assignments, discussions, pages, files, modules, people, grades, syllabus) |
| R | Groups |
| Q | A quiz (the quiz list, taking one, its results) |
| S | Handing work in |
| F | Feedback on work handed in |
| P | The guided setup and Personalize |
| X | A page Canvas draws itself (Simpl switched off, or a page Simpl leaves to Canvas) |

## What

| Code | Meaning | What to try |
| --- | --- | --- |
| 401 | Canvas says you are signed out, or the thing is not yours to see | Sign in to Canvas again |
| 403 | Canvas says you are not allowed (a locked item, an access code, a closed quiz) | Check the item in stock Canvas |
| 404 | Canvas says it is not there (deleted, unpublished, or a wrong address) | Check the link |
| 409, 422 | Canvas refused the change (a clash, or something it will not accept) | Try again; report it if it repeats |
| 429 | Canvas is asking for fewer requests for a moment | Wait a minute |
| 500, 502, 503, 504 | Something went wrong on Canvas's side | Try again later |
| NET | No answer at all: offline, or the connection dropped | Check the connection |
| LOAD | A part of Simpl did not load (the setup, a tool, the quiz screen) | Reload the page; if it repeats, quit and reopen the browser |
| APP | Nothing failed underneath: Simpl's own doing | Report it, with the code |

A code is read from the error in hand when there is one, and otherwise from a failure of the last
few seconds (a request to Canvas, a part of Simpl loading). An error shown with nothing failed
underneath is `APP`.

An error Canvas's own page put up — one of its flash banners, which Simpl never lets draw over its
screens and passes on as a toast instead — is `SC-X-…`, by what its words say: `401` signed out,
`403` not allowed, `422` not saved, and `APP` for any other. Only a banner Canvas put up as an error
or a warning is coded by its words: a success or a notice that names a permission is no error. In the
apps with their own screens (iPhone, Mac, Windows) Canvas's page is hidden under them, so none of its
banners comes up or is noted.

## The report page's own codes

The report page on simplcourses.com has codes of its own, `SC-B-…`, for a report it could not
take or pass on:

| Code | Meaning |
| --- | --- |
| SC-B-KIND | No kind of report chosen |
| SC-B-TEXT | No words written |
| SC-B-EMAIL, SC-B-PHONE | The email address or phone number does not look right |
| SC-B-SHOTS, SC-B-SIZE, SC-B-415 | More than five screenshots, one over 8 MB, or a file that is not a picture |
| SC-B-400, SC-B-405, SC-B-413 | The report did not arrive as a form, was not sent with POST, or was too large |
| SC-B-429 | Too many reports from one place in ten minutes |
| SC-B-503 | Reports are not switched on yet (the page offers a GitHub issue instead) |
| SC-B-502 | The report could not be passed on; try again in a minute |
| SC-B-NET | The page got no answer from simplcourses.com |

## Simpl Community's own codes

Simpl Community (simplcourses.com/community, and Community in Appearance and on the Tools page) has codes of its own.
They name the service, not a screen: `SC-C-…` here is not a course's page, nor `SC-T-…` To Do.

`SC-C-…`, the service and its themes (`site/src/community.js`):

| Code | Meaning |
| --- | --- |
| SC-C-503 | Simpl Community is not switched on yet (its storage is not bound) |
| SC-C-400, SC-C-405 | The request was not what was expected, or not sent with POST |
| SC-C-401, SC-C-403 | The review: not signed in, or a decision not sent from the review page |
| SC-C-404, SC-C-409 | No such theme or tool; or it was decided already (a starter tool can't be taken down) |
| SC-C-429 | Too many at once from one place: three themes or five tools an hour, eight passcode tries in a quarter of an hour |
| SC-C-FULL | So many are waiting to be looked at that no more can come in for now |
| SC-C-KEY | The review has no passcode set yet |
| SC-C-THEME, SC-C-NAME, SC-C-COLOUR, SC-C-SCENE, SC-C-EMPTY | A theme sent in that could not be read, with no name, a colour Simpl can't use, a drawing Simpl doesn't have, or neither a colour nor a picture |
| SC-C-PHOTO, SC-C-TYPE, SC-C-BIG | A theme's photo named but not sent, not JPEG, PNG or WebP, or too big |

`SC-T-…`, a Community tool refused (`validateTool` in `site/public/community/tool-format.js`, the reference every app's
copy follows: `docs/community-tools.md`). The site refuses a tool sent in with one of these, with the part at fault;
an app refuses to add a tool that fails its own check the same way.

| Code | Meaning |
| --- | --- |
| SC-T-JSON, SC-T-BIG | The tool could not be read, or is too big (over 32,768 characters as JSON) |
| SC-T-FORMAT | Not a Simpl tool, or a later format ("Needs a newer Simpl") |
| SC-T-KEY | Something format 1 doesn't have (the site checks strictly; the apps pass over it) |
| SC-T-NAME, SC-T-SUMMARY, SC-T-GROUP, SC-T-SYMBOL, SC-T-COLOUR | No name, no line saying what it does, a group, a symbol or a colour that isn't one of Simpl's |
| SC-T-INPUTS, SC-T-INPUT, SC-T-KIND, SC-T-LABEL | Not 1 to 16 inputs, one that can't be read or isn't a number, a choice or a switch, or one without a label |
| SC-T-NUMBER, SC-T-RANGE, SC-T-OPTIONS | A number input's default, min, max or step that isn't a number, a min above its max, a choice without 2 to 12 distinct options |
| SC-T-ID | A name in formulas that isn't a–z, 0–9 and _, is one Simpl keeps (pi, e, true, false, a function), or is used twice |
| SC-T-OUTPUTS, SC-T-OUTPUT, SC-T-DECIMALS | Not 1 to 12 results, one that can't be read, or decimals that aren't 0 to 10 |
| SC-T-FORMULA, SC-T-NAME-USE | A formula that can't be read (a function Simpl doesn't have, the wrong number of values), or one that reads a name that isn't there yet |
| SC-T-TABLE | A table without 1 to 6 named columns, 1 to 100 rows, or one cell per column in every row |

## For developers

- `extension/lib/errors.js` — the codes: `failed(kind)` (said by the request layer and the
  on-demand loader when something fails), `codeFor(err)`, `note(code)`, `recent()`.
- `extension/content/app/ui.js` — `errorBox(words, err)` and `toast(words, { error: true, err })`
  put the code on the element as `data-code`; `app.css` draws it after the words, so it is never
  part of the words themselves.
- `site/src/report.js` — the report page's handler and its `SC-B-…` codes.
- `site/src/community.js` — Simpl Community's endpoints and their `SC-C-…` codes; `site/public/community/tool-format.js`
  — a tool's check and its `SC-T-…` codes.
