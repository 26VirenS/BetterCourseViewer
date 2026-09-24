# What Canvas shows that Simpl does not

A student-facing inventory of Canvas LMS against Simpl Courses, from the code as it stands (the
endpoints the extension calls, the screens it draws, the fields it shows). Three states:

- **Drawn** — Simpl's own screen.
- **Canvas's page** — reachable, but shown as Canvas drew it (the punch-through: Canvas's header, left
  nav, breadcrumbs and footer hidden, the page's main column laid into Simpl's shell).
- **Missing** — not reachable from Simpl at all (no link; only by typing the address).

## 0. The short list, by how often a student meets it

1. **Grades, per row** — the rubric and the instructor's comment itself. Missing. (Since 2.95.0 the
   rows carry the class mean/high/low, the comment count, the late penalty, the letter grade, and
   the total says final vs current; a score the teacher has not posted is "Not graded" until it is.)
2. **Global announcements** — what the school posts to everyone (outages, deadlines). Missing:
   `account_notifications` is never called.
3. **Inbox actions** — archive, delete, mark unread, forward, attach a file to a reply. Missing.
   (Reading is complete: attachments, voice and video notes, and forwarded messages are drawn.)
4. **Discussions** — like, subscribe, edit or delete your own post, attach a file. Missing.
5. **Calendar** — create or edit a personal event, planner notes on the grid. Missing.
6. **Files** — upload, sort. Missing (the locked/hidden state and the uploader are drawn).
7. **Grading periods** — a term split into periods, with a score per period. Missing.
8. **Peer reviews** — the ones assigned to you. Missing.

Drawn since 2.95.0, and so off this list: To Do row flags (Missing / Late / Excused / Feedback / New),
the quiz list's score and status, the modules' prerequisites, sequential locks and per-item
requirement, the account menu's e-mail and pronouns, and every row's status words.

## 1. Coverage by area

### Global

| Canvas | Simpl | Notes |
|---|---|---|
| Dashboard: card view, list (planner) view, recent activity | **Drawn** | Six counters, weekly workload, cards/list/activity, Search everything; the list keeps to the favourites like the cards; activity rows read the posted score, the comment, posted_at |
| Dashboard: card reordering, unread counts on the card's quick-link icons | Missing | Cards carry the nickname, code · term, the teacher and the grade chip |
| Global announcements (account notifications) | Missing | `accounts/:id/account_notifications` never called |
| Recent Feedback (dashboard sidebar) | Missing | Notifications derives "Graded"/"Feedback" from the activity stream instead |
| Coming Up (dashboard sidebar) | Partial | Due today / this week / tomorrow counters cover it |
| All Courses (favourites, terms, past/future enrollments) | **Drawn** | |
| Groups list | **Drawn** | |
| Calendar: week/month/agenda, course/group/personal calendars | **Drawn** | Ten-calendar limit enforced; handed-in work struck through, missing work marked, points and place on rows |
| Calendar: Scheduler / Find appointment (reserve, cancel) | **Drawn** | Group sign-ups open Canvas's page |
| Calendar: create/edit personal events, planner notes on the grid, undated list, iCal feed, `?event_id=` deep links | Missing | |
| To Do (planner) with personal tasks | **Drawn** | An Overdue group two weeks back, then today onward; rows say Missing / Late / Excused / Feedback / New (planner `submissions` flags, `new_activity`) |
| To Do: edit a task, task notes | Missing | No PUT for planner notes |
| Inbox: inbox/unread/starred/sent/archived, course filter, reader, reply, compose with recipient search | **Drawn** | Rows: course, message count, attachment mark, time; reader: attachments, voice/video notes, forwarded messages; recipients say the course you share. First 50 conversations only (no paging) |
| Inbox: archive/unarchive, delete, mark unread, forward, attachments and media on compose/reply, bulk actions, address book browsing | Missing | |
| Notifications (Canvas has no page; Simpl's own feed) | **Drawn** | Overdue (two weeks back), Due soon, Graded (posted only), Feedback, Messages, Discussions, Announcements, System (with its category) |
| History | **Drawn** (sheet) | |
| Help menu | **Drawn** (sheet) | Report a problem is Canvas's form |
| Account: profile (bio, links), settings (language, time zone, feature options, tokens, registered services), notification preferences | Canvas's page | Links from the account menu; the row and the menu show your pronouns and e-mail (`users/self/profile`) |
| Account: personal files (My Files, quota), ePortfolios, QR for mobile login, Shared content, Observing | Missing | Canvas's left nav is hidden on those pages, and no Simpl link exists |
| Search everything | **Drawn** | Courses, assignments, announcements, pages, discussions, files, people, Wikipedia; "/" commands (submit, download, convert, open a tool, todo…), a row's actions, sums, ⌘K from any screen — Canvas has no equivalent |

### Inside a course

| Canvas | Simpl | Notes |
|---|---|---|
| Home (front page / modules / assignments / syllabus / stream, as the course chose) | **Drawn** | Right column: stream/calendar/notifications links, course To Do with Ignore |
| Home: Coming Up, Recent Feedback, announcements on the home page | Missing | |
| Announcements list and detail | **Drawn** | Rows: author, reply and unread counts, sections. Likes on an announcement: missing |
| Assignments list (sections, by type with weights, search, status badges) | **Drawn** | "10 pts" until scored; Missing / Late / Excused / Submitted / Graded (posted only); letter and pass-fail grades; Opens / Closed from the lock window |
| Assignment page (due, points, availability, attempts, rubric, mark as done, previous/next, external-tool launch) | **Drawn** | Class mean/high/low, comment count, late penalty, letter grade, extra attempts, the lock reason for every type |
| Hand in: file upload (with conversion), text entry (local draft), website URL, homework LTI tools, comment, receipt | **Drawn** | Also from the search box (/submit, or Submit on a result row) in a sheet over any page |
| Hand in: media recording, student annotation | Canvas's page | Rows send you there |
| Peer reviews, group-assignment flag, Turnitin/originality report | Missing | |
| Submission feedback: score, attempts, files, text, URL, comments per attempt (words, files, voice and video notes), late penalty, comment composer | **Drawn** | |
| Submission details page (DocViewer annotations) | Canvas's page | |
| Discussions list (pinned/open/closed, unread, replies, last post, points, to-do date, open until / closed, search) | **Drawn** | New discussion opens Canvas's editor |
| Discussion thread (replies threaded, reply, must-post-first, auto mark read) | **Drawn** | Depth shown to two levels |
| Discussions: like/rate, subscribe, per-entry unread, edit/delete own post, attachments or rich text in replies, group discussions (child topics), graded-discussion rubric, checkpoints | Missing | |
| Grades (total to the decimal, final vs current, group rings, legend, weights, rows with status, letter and pass-fail grades, class mean/high/low, what-if) | **Drawn** | Posted scores only; a hidden total is said to be hidden |
| Grades: per-row comments and rubric, grading periods, "graded only" toggle, sorting | Missing | |
| People (roles as the course names them, search, sections, pronouns), Groups sub-view | **Drawn** | Join/leave a self-sign-up group: missing; person page: Canvas's |
| Pages list and page (front page, dates, body, mark as done, links, lock explanation) | **Drawn** | Revision history, edit, search on the tab: missing |
| Files (folders with counts, viewer, download, open in tab, uploader, locked/hidden/opens badges) | **Drawn** | Upload, usage rights, sort, recursive search: missing |
| Quizzes list (score and status per row, time limit, attempts, lock window, New Quizzes as rows), quiz page, taking (classic), feedback | **Drawn** | Calculated/file-upload questions, LockDown: Canvas's page; New Quizzes are LTI tools |
| Modules (requirements done, locked until, items, sort, open/close all, mark as done, prerequisites, sequential progress, per-item requirement and lock reason) | **Drawn** | Module prev/next: missing |
| Syllabus (body + course summary of dated assignments) | **Drawn** | Events and undated items in the summary: missing |
| Outcomes (mastery), Rubrics, Conferences, Collaborations, Chat, Attendance | Canvas's page / campus tools | In the rail; Canvas draws them |
| Course notifications settings | Canvas's page | |
| LTI tools (Zoom, New Quizzes, publisher tools…) | Own tab with Simpl's bar | |

### Only in Simpl

Tools (citations, focus timer, calculators, periodic table, grade needed, converter, PDF tools, OCR, flashcards) and widgets of your own (Tools → Add a widget) have no Canvas counterpart; the coverage tables above leave them out.

### Groups

Home, Announcements, Discussions, People, Pages and Files are drawn; Conferences, Collaborations and
tools are Canvas's page. Joining or leaving a group (`groups/:id/memberships`) is missing.

## 2. Extra data the API already offers (fetched or a call away)

| Data | Endpoint / field | Where it would fit |
|---|---|---|
| Your bio and time zone | `users/self/profile` (fetched since 2.95.0 for the e-mail and pronouns) | Account menu |
| Global announcements | `accounts/self/account_notifications` | Dashboard banner / Notifications "System" |
| Missing submissions | `users/self/missing_submissions?include[]=planner_overrides,course` | Overdue card (one request instead of one per course) |
| Upcoming events | `users/self/upcoming_events` | Dashboard "Coming up" |
| Grading periods and per-period scores | `courses/:id/grading_periods`, `enrollments?include[]=grading_periods` | Grades period picker |
| The late policy itself | `courses/:id/late_policy` (the deduction and the days late are shown from the submission) | Assignment page: "x% a day" |
| Rubric per row | `assignment_groups?include[]=rubric` | Grades rows (rubric icon) |
| Module item sequence | `module_item_sequence` | Modules: prev/next |
| Discussion ratings, subscription, per-entry read state | `discussion_topics/:id/view` (already fetched: `read_state`, `rating_sum`), `entries/:id/rating`, `subscribed` | Thread |
| Group discussion children | `discussion_topics/:id` → `group_topic_children` | Thread ("your group's copy") |
| Page revisions | `pages/:slug/revisions` | Page view |
| File usage rights | `files?include[]=usage_rights` | Files rows |
| Personal files and quota | `users/self/folders/root`, `users/self/files/quota` | A "My files" section |
| Conversation paging, archive, delete | `conversations?page=`, `PUT conversations/:id {workflow_state}`, `DELETE` | Inbox |
| Course card positions | `users/self/dashboard_positions` | Card reordering |
| Calendar event id deep links, undated events | `?event_id=`, `calendar_events?undated=true` | Calendar |
| Content shares, ePortfolios | `users/self/content_shares`, `users/self/eportfolios` | Account |

## 3. Endpoints never called

`accounts/:id/account_notifications`, `users/self/upcoming_events`, `users/self/todo`,
`users/self/missing_submissions`, `users/self/dashboard_positions`,
`users/self/settings`, `communication_channels`, notification preferences, `users/self/files`,
`users/self/folders`, `users/self/files/quota`, `eportfolios`, `content_shares`, `media_objects`,
`grading_periods`, `enrollments`, `outcome_results`, `outcome_rollups`, `rubrics`, `peer_reviews`,
`late_policy`, `conferences`, `collaborations`, page `revisions`, discussion entry rating/subscribe/
read state, `modules/:m/items/:i/mark_read`, calendar event create/update, `calendar_events?undated`,
conversations `batch_update`/delete/archive/forward, `groups/:id/memberships`.
