# What Canvas shows that Simpl does not

A student-facing inventory of Canvas LMS against Simpl Courses, from the code as it stands (the
endpoints the extension calls, the screens it draws, the fields it shows). Three states:

- **Drawn** — Simpl's own screen.
- **Canvas's page** — reachable, but shown as Canvas drew it (the punch-through: Canvas's header, left
  nav, breadcrumbs and footer hidden, the page's main column laid into Simpl's shell).
- **Missing** — not reachable from Simpl at all (no link; only by typing the address).

## 0. The short list, by how often a student meets it

1. **Grades, per row** — the instructor's comment, the rubric, the class mean/high/low, the late
   penalty, final vs current score. Missing, and most of the data is already fetched
   (`score_statistics`, `computed_final_score`, `submission_comments`).
2. **Global announcements** — what the school posts to everyone (outages, deadlines). Missing:
   `account_notifications` is never called.
3. **To Do row flags** — Missing / Late labels, "new activity" and "feedback" marks. Missing; the
   planner already returns `new_activity` and `has_feedback`.
4. **Inbox actions** — archive, delete, mark unread, forward, attachments. Missing.
5. **Discussions** — like, subscribe, edit or delete your own post, attach a file. Missing.
6. **Quizzes list** — your score and status on each row. Missing (the quiz page has it).
7. **Modules** — prerequisites, sequential locks, the requirement type per item ("view", "submit",
   "score ≥ x"). Missing; `completion_requirement` is already fetched.
8. **Calendar** — create or edit a personal event, planner notes on the grid. Missing.
9. **Account** — your email and pronouns in the menu (`users/self/profile`). Missing.
10. **Files** — upload, sort, the locked/hidden state. Missing.

## 1. Coverage by area

### Global

| Canvas | Simpl | Notes |
|---|---|---|
| Dashboard: card view, list (planner) view, recent activity | **Drawn** | Six counters, weekly workload, cards/list/activity, Search everything |
| Dashboard: card reordering, nickname on the card, unread counts on the card's quick-link icons | Missing | Nicknames can be set (All Courses, setup, Settings, phone); the card does not show them |
| Global announcements (account notifications) | Missing | `accounts/:id/account_notifications` never called |
| Recent Feedback (dashboard sidebar) | Missing | Notifications derives "Graded"/"Feedback" from the activity stream instead |
| Coming Up (dashboard sidebar) | Partial | Due today / this week / tomorrow counters cover it |
| All Courses (favourites, terms, past/future enrollments) | **Drawn** | |
| Groups list | **Drawn** | |
| Calendar: week/month/agenda, course/group/personal calendars | **Drawn** | Ten-calendar limit enforced |
| Calendar: Scheduler / Find appointment (reserve, cancel) | **Drawn** | Group sign-ups open Canvas's page |
| Calendar: create/edit personal events, planner notes on the grid, undated list, iCal feed, `?event_id=` deep links | Missing | |
| To Do (planner) with personal tasks | **Drawn** | Window starts today: past-due items only on the Dashboard's Overdue card |
| To Do: edit a task, task notes, planner "new activity"/"feedback" flags, Missing/Late labels on rows | Missing | `has_feedback`, `new_activity` never read; no PUT for planner notes |
| Inbox: inbox/unread/starred/sent/archived, course filter, reader, reply, compose with recipient search | **Drawn** | First 50 conversations only (no paging) |
| Inbox: archive/unarchive, delete, mark unread, forward, attachments and media on compose/reply, media comments in the reader, bulk actions, address book browsing | Missing | |
| Notifications (Canvas has no page; Simpl's own feed) | **Drawn** | Overdue, Due soon, Graded, Feedback, Announcements, System |
| History | **Drawn** (sheet) | |
| Help menu | **Drawn** (sheet) | Report a problem is Canvas's form |
| Account: profile (bio, links, pronouns), settings (language, time zone, feature options, tokens, registered services), notification preferences | Canvas's page | Links from the account menu; the menu's second line meant to show your email never does (the profile is not fetched) |
| Account: personal files (My Files, quota), ePortfolios, QR for mobile login, Shared content, Observing | Missing | Canvas's left nav is hidden on those pages, and no Simpl link exists |
| Search everything | **Drawn** | Courses, assignments, announcements, pages, discussions, files, people, Wikipedia; "/" commands (submit, download, convert, open a tool, todo…), a row's actions, sums, ⌘K from any screen — Canvas has no equivalent |

### Inside a course

| Canvas | Simpl | Notes |
|---|---|---|
| Home (front page / modules / assignments / syllabus / stream, as the course chose) | **Drawn** | Right column: stream/calendar/notifications links, course To Do with Ignore |
| Home: Coming Up, Recent Feedback, announcements on the home page | Missing | |
| Announcements list and detail | **Drawn** | Replies/likes on an announcement: missing |
| Assignments list (sections, by type with weights, search, status badges) | **Drawn** | |
| Assignment page (due, points, availability, attempts, rubric, mark as done, previous/next, external-tool launch) | **Drawn** | |
| Hand in: file upload (with conversion), text entry (local draft), website URL, homework LTI tools, comment, receipt | **Drawn** | Also from the search box (/submit, or Submit on a result row) in a sheet over any page |
| Hand in: media recording, student annotation | Canvas's page | Rows send you there |
| Peer reviews, group-assignment flag, Turnitin/originality report, late-penalty deduction (`points_deducted`) | Missing | |
| Submission feedback: score, attempts, files, text, URL, comments per attempt, comment composer | **Drawn** | Attachments on comments: missing |
| Submission details page (DocViewer annotations) | Canvas's page | |
| Discussions list (pinned/open/closed, unread, replies, last post, search) | **Drawn** | New discussion opens Canvas's editor |
| Discussion thread (replies threaded, reply, must-post-first, auto mark read) | **Drawn** | Depth shown to two levels |
| Discussions: like/rate, subscribe, per-entry unread, edit/delete own post, attachments or rich text in replies, group discussions (child topics), graded-discussion rubric, checkpoints | Missing | |
| Grades (total, group rings, legend, weights, rows with status, what-if) | **Drawn** | |
| Grades: per-row comments and rubric, score details (mean/high/low), grading periods, final vs current, "graded only" toggle, late penalty, unposted icon, sorting | Missing | `score_statistics` and `computed_final_score` are fetched and never shown |
| People (roles, search, sections, pronouns), Groups sub-view | **Drawn** | Join/leave a self-sign-up group: missing; person page: Canvas's |
| Pages list and page (front page, dates, body, mark as done, links) | **Drawn** | Revision history, edit, search on the tab: missing |
| Files (folders, viewer, download, open in tab) | **Drawn** | Upload, usage rights, locked/hidden state, sort, recursive search: missing |
| Quizzes list, quiz page, taking (classic), feedback | **Drawn** | Your score/status in the list: missing; calculated/file-upload questions, LockDown: Canvas's page; New Quizzes are LTI tools |
| Modules (requirements done, locked until, items, sort, open/close all, mark as done) | **Drawn** | Prerequisites, sequential progress, per-item requirement type, per-item lock, module prev/next: missing |
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
| Class score statistics (mean, high, low) | `assignments/:id?include[]=score_statistics` (already fetched) | Assignment page grade chip; Grades rows |
| Final vs current score, letter grade | `computed_final_score`, `computed_current_grade` (already mapped) | Grades header |
| Your pronouns, email, login id, bio, time zone | `users/self/profile` | Account menu (the second line), setup |
| Global announcements | `accounts/self/account_notifications` | Dashboard banner / Notifications "System" |
| Missing submissions | `users/self/missing_submissions?include[]=planner_overrides,course` | Overdue card (one request instead of one per course) |
| Upcoming events | `users/self/upcoming_events` | Dashboard "Coming up" |
| Planner flags | `planner/items` → `new_activity`, `has_feedback`, `submissions.needs_grading` | To Do rows, Notifications |
| Grading periods and per-period scores | `courses/:id/grading_periods`, `enrollments?include[]=grading_periods` | Grades period picker |
| Late policy and deductions | `courses/:id/late_policy`, `submission.points_deducted`, `seconds_late` | Grades rows, feedback screen |
| Rubric per row, submission comments count | `assignment_groups?include[]=rubric`, `submission.submission_comments` | Grades rows (comment/rubric icons) |
| Module item requirements, prerequisites, sequence | `modules?include[]=items,content_details` (already fetched: `completion_requirement`, `prerequisite_module_ids`), `module_item_sequence` | Modules: "View / Submit / Score ≥ x" per item, prev/next |
| Discussion ratings, subscription, per-entry read state | `discussion_topics/:id/view` (already fetched: `read_state`, `rating_sum`), `entries/:id/rating`, `subscribed` | Thread |
| Group discussion children | `discussion_topics/:id` → `group_topic_children` | Thread ("your group's copy") |
| Page revisions | `pages/:slug/revisions` | Page view |
| File usage rights, locked/hidden | `files?include[]=usage_rights` (fields on the file) | Files rows |
| Personal files and quota | `users/self/folders/root`, `users/self/files/quota` | A "My files" section |
| Quiz score/status in the list | `quizzes/:id/submissions` (already used on the quiz page) | Quizzes list |
| Conversation paging, archive, delete | `conversations?page=`, `PUT conversations/:id {workflow_state}`, `DELETE` | Inbox |
| Card term, teachers, current term | `dashboard_cards.term`, `courses.teachers`, `enrollment_term` (already mapped) | Course cards, course header |
| Course card positions | `users/self/dashboard_positions` | Card reordering |
| Calendar event id deep links, undated events | `?event_id=`, `calendar_events?undated=true` | Calendar |
| Content shares, ePortfolios | `users/self/content_shares`, `users/self/eportfolios` | Account |

## 3. Endpoints never called

`accounts/:id/account_notifications`, `users/self/upcoming_events`, `users/self/todo`,
`users/self/missing_submissions`, `users/self/dashboard_positions`, `users/self/profile`,
`users/self/settings`, `communication_channels`, notification preferences, `users/self/files`,
`users/self/folders`, `users/self/files/quota`, `eportfolios`, `content_shares`, `media_objects`,
`grading_periods`, `enrollments`, `outcome_results`, `outcome_rollups`, `rubrics`, `peer_reviews`,
`late_policy`, `conferences`, `collaborations`, page `revisions`, discussion entry rating/subscribe/
read state, `modules/:m/items/:i/mark_read`, calendar event create/update, `calendar_events?undated`,
conversations `batch_update`/delete/archive/forward, `groups/:id/memberships`.
