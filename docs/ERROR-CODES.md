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

## For developers

- `extension/lib/errors.js` — the codes: `failed(kind)` (said by the request layer and the
  on-demand loader when something fails), `codeFor(err)`, `note(code)`, `recent()`.
- `extension/content/app/ui.js` — `errorBox(words, err)` and `toast(words, { error: true, err })`
  put the code on the element as `data-code`; `app.css` draws it after the words, so it is never
  part of the words themselves.
- `site/src/report.js` — the report page's handler and its `SC-B-…` codes.
