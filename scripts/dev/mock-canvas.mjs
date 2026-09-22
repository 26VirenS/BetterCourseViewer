#!/usr/bin/env node
// A fake Canvas for local testing: Canvas-like HTML pages plus the REST
// endpoints the redesigned interface reads. Dates are relative to "now" so
// the dashboard always has something due today.
// Usage: node scripts/dev/mock-canvas.mjs [port]
import http from 'node:http';

const port = Number(process.argv[2] || process.env.PORT || 8787);
const now = new Date();
const H = 3600e3;
const D = 24 * H;
const at = (dayOffset, hour = 23, minute = 59) => {
  const d = new Date(now);
  d.setDate(d.getDate() + dayOffset);
  d.setHours(hour, minute, 0, 0);
  return d.toISOString();
};
const ago = (ms) => new Date(now.getTime() - ms).toISOString();
// the session's CSRF token, as Canvas keeps it: in the _csrf_token cookie, URL-encoded (base64 has + / =)
const CSRF = 'mock+csrf/token=';

// ---- courses ----------------------------------------------------------------------
const term = { id: '1', name: 'Fall 2026', start_at: ago(20 * D), end_at: at(100) };
const courses = [
  { id: '101', name: 'F26-MATH 021 20', code: 'MATH-021-20', color: '#34c759', score: 92.4, grade: 'A-', teacher: 'Yue Lei', section: 'Lecture-20', weighted: true, default_view: 'wiki' },
  { id: '102', name: 'F26-PHYS 008 01', code: 'PHYS-008-01', color: '#30b0c7', score: 81, grade: 'B-', teacher: 'Dana Okafor', section: 'Lecture-01', weighted: false, default_view: 'modules' },
  { id: '103', name: 'F26-PHYS 008HL 01/PHYS 008L 01', code: 'PHYS-008HL', color: '#ff2d55', score: null, grade: null, teacher: 'Dana Okafor', section: 'Section 01', weighted: false, default_view: 'wiki' },
  { id: '104', name: 'F26-SPRK 010 103', code: 'SPRK-010-103', color: '#ff9500', score: 88, grade: 'B+', teacher: 'Ana Ruiz', section: 'Section 103', weighted: false, default_view: 'syllabus' },
  { id: '105', name: 'F26-WRI 010 20', code: 'WRI-010-20', color: '#c8901c', score: 95, grade: 'A', teacher: 'Marcus Bell', section: 'Section 20', weighted: false, default_view: 'assignments' },
  { id: '106', name: 'F26-CHEM 002 01', code: 'CHEM-002-01', color: '#af52de', score: null, grade: null, teacher: 'Lin Zhao', section: 'Lecture-01', favorite: false },
  { id: '201', name: 'Academic Success Resource Site (2026-27)', code: 'ASRS', color: '#1e7a37', score: null, grade: null, teacher: 'Student Success', section: 'All', favorite: false, term: { id: '9', name: 'Collaboration team' } },
  { id: '202', name: 'Placement Exam: Chemistry', code: 'PLACE-CHEM', color: '#5856d6', score: null, grade: null, teacher: 'Placement Office', section: 'All', favorite: false, term: { id: '9', name: 'Collaboration team' } },
  { id: '301', name: 'S26-CSE 022 01', code: 'CSE-022-01', color: '#0a84ff', score: 97, grade: 'A', teacher: 'Priya Nair', section: 'Lecture-01', favorite: false, past: true, term: { id: '0', name: 'Spring 2026', start_at: ago(220 * D), end_at: ago(100 * D) } },
];
const favorites = new Set(['101', '102', '103', '104', '105']);
const nicknames = new Map(); // course id → the student's nickname; Canvas then serves it as the name, the real one as original_name
const courseById = (id) => courses.find((c) => c.id === String(id));
const fullCourse = (c) => ({
  id: c.id, name: nicknames.get(c.id) || c.name, course_code: c.code, original_name: nicknames.has(c.id) ? c.name : undefined, term: c.term || term, is_favorite: favorites.has(c.id), default_view: c.default_view || 'wiki',
  workflow_state: c.past ? 'completed' : 'available', start_at: null, end_at: null, apply_assignment_group_weights: !!c.weighted,
  enrollments: [{ type: 'student', role: 'StudentEnrollment', enrollment_state: c.past ? 'completed' : 'active', computed_current_score: c.score, computed_current_grade: c.grade, computed_final_score: c.score }],
  teachers: [{ id: `t${c.id}`, display_name: c.teacher }], sections: [{ id: `s${c.id}`, name: c.section }], image_download_url: null,
});

// ---- assignments per course --------------------------------------------------------
// [id, name, group, possible, earned, dueOffsetDays, dueHour, submittedOffset|null, extra]
const A = {
  101: [
    ['1001', 'Lec01-PreQuiz', 'Effort', 16, 13, -13, 10.5, -14, { quiz: true }],
    ['1002', 'Lec02-PreQuiz', 'Effort', 17, 11, -10, 10.5, -12, { quiz: true }],
    ['1003', 'Dis00', 'Collaboration', 10, 10, -7, 23.98, null, {}],
    ['1004', 'Qz00', 'Discussion Quizzes', 10, 10, -7, 23.98, null, { quiz: true }],
    ['1005', 'Skills_Check', 'Discussion Quizzes', 14, 8, -7, 23.98, null, { quiz: true, omit: true }],
    ['1006', 'Functions and Their Representations', 'Effort', 30, 30, -6, 23.98, -12, { held: true }],
    ['1007', 'Lec05-PreQuiz', 'Effort', 19, 19, -1, 10.5, -2, { quiz: true }],
    ['1008', 'Transformation of Functions and Sinusoidal Functions', 'Effort', 30, 0, -1, 23.98, 0, { late: true }],
    ['1009', 'Dis01', 'Collaboration', 10, null, 0, 23.98, null, { rubric: true }],
    ['1010', 'Qz01', 'Discussion Quizzes', 10, null, 0, 23.98, null, { quiz: true }],
    ['1011', 'Lec06-PreQuiz', 'Effort', 17, null, 1, 10.5, null, { quiz: true }],
    ['1012', 'Composition of Functions', 'Effort', 30, null, 1, 23.98, null, {}],
    ['1013', 'project01', 'Collaboration', 20, null, 3, 23.98, null, {}],
    ['1014', 'Lec07-PreQuiz', 'Effort', 20, null, 4, 10.5, null, { quiz: true }],
    ['1015', 'Midterm 1', 'Midterms', 100, null, 15, 10.5, null, {}],
    ['1016', 'Midterm 2', 'Midterms', 100, null, 43, 10.5, null, {}],
    ['1017', 'Final Exam', 'Final', 200, null, 92, 11.5, null, {}],
  ],
  102: [
    ['2001', 'Lab 1 report', 'Labs', 20, 18, -7, 23.98, -8, {}],
    ['2002', 'W2 HW', 'Homework', 15, null, -1, 23.98, null, {}],
    ['2018', 'Lab safety check', 'Homework', 10, 10, 6, 12, -10, { gradedDay: -9 }], // graded early: due next week, already marked
    ['2019', 'Lec08-PreQuiz', 'Homework', 20, null, 8, 10.5, null, { quiz: true, code: 'PHYS8' }], // a restricted quiz: an access code the student is not told
    ['2020', 'EXTRA POINTS 1', 'Homework', 2, null, 10, 12.5, null, { quiz: true, survey: 'graded_survey' }], // a graded survey: points for taking part
    ['2022', 'Lec09-PreQuiz', 'Homework', 20, null, 9, 10.5, null, { quiz: true, code: 'PHYS9', codeHidden: true }], // an access code Canvas does not even announce
    ['2003', 'Lab 2', 'Labs', 20, null, 2, 23.98, null, {}],
    ['2004', 'W3 HW', 'Homework', 15, null, 6, 23.98, null, {}],
  ],
  103: [
    ['3001', 'Prelab 2', 'Prelabs', 15, 15, -4, 23.98, -5, {}],
    ['3002', 'Prelab 3', 'Prelabs', 15, null, 3, 23.98, null, {}],
  ],
  104: [
    ['4001', 'Week 1 reflection', 'Assignments', 10, 10, -6, 23.98, -7, { rubric: true }],
    // the mockup's example: file or text or link, four file types, open for a week, unlimited attempts
    ['4002', 'Week 2 Post Class Assignment: GC articles', 'Assignments', 10, null, 0, 23.98, null, { types: ['online_upload', 'online_text_entry', 'online_url'], ext: ['pdf', 'docx', 'png', 'jpg'], window: true, attempts: -1, description: '<p>Submit the Grand Challenge you chose with an explanation of why it matters, plus three scientific papers or news articles in APA or MLA format. See the <a class="instructure_file_link" title="Course Syllabus.pdf" href="/courses/101/files/f1?wrap=1">citation guide</a> before you start.</p><p><span style="color: #2d3b45;">Bring the printed rubric to lab — the TA marks it in person.</span></p>' }],
    ['4003', 'Knewton Alta: Unit 2', 'Assignments', 20, null, 5, 23.98, null, { tool: 'https://tool.example.com/launch' }],
  ],
  105: [
    ['5001', 'Journal #1', 'Journals', 5, 5, -6, 23.98, -6, {}],
    ['5002', 'Research Day Activity: Choosing a Field Site', 'Activities', 5, null, 0, 23.98, null, {}],
    ['5003', 'Journal #2', 'Journals', 5, null, 1, 23.98, null, { discussion: '7503' }], // a graded discussion, in its module as the topic (with a must_mark_done requirement)
  ],
  106: [], 201: [], 202: [], 301: [['9001', 'Final project', 'Projects', 100, 97, -110, 23.98, -111, {}]],
};
const GROUPS = { 101: [['Discussion Quizzes', 18], ['Midterms', 57], ['Final', 25], ['Effort', 0], ['Collaboration', 0], ['Coursework (Knewton Alta)', 0]] };
const rubric = [
  // a criterion written as rich text, the way Canvas's rubric editor stores one, and long enough that
  // it has to be clamped
  { id: 'c1', description: 'Correctness', long_description: '<p>&bull; Every answer is correct<br/>&bull; Units on each one<br/>&bull; Working shown for every step, in the order it was done, with the reasoning written out so a marker can follow it without having to guess at anything</p>', points: 6, ratings: [{ id: 'r1', description: 'Full marks', points: 6 }, { id: 'r2', description: 'Partial', points: 3 }, { id: 'r2b', description: 'No marks', points: 0 }] },
  { id: 'c2', description: 'Work shown', long_description: 'Steps are legible and complete.', points: 4, ratings: [{ id: 'r3', description: 'Full marks', points: 4 }, { id: 'r4', description: 'Partial', points: 2 }, { id: 'r4b', description: 'No marks', points: 0 }] },
];
const scoreOverrides = new Map(); // assignment id → a score that landed after the seed (POST /__mock/score)
function assignmentObj(courseId, row) {
  const [id, name, group, possible, seeded, dueDay, dueHour, subDay, extra] = row;
  const earned = scoreOverrides.has(String(id)) ? scoreOverrides.get(String(id)) : seeded;
  const c = courseById(courseId);
  const groups = (GROUPS[courseId] || [[Object.keys(groupNames(courseId))[0], 0]]);
  const gIdx = groups.findIndex(([g]) => g === group);
  const due = at(dueDay, Math.floor(dueHour), Math.round((dueHour % 1) * 60));
  const submitted = subDay !== null || earned !== null;
  const submission = {
    id: `s${id}`, assignment_id: id, workflow_state: earned !== null ? 'graded' : submitted ? 'submitted' : 'unsubmitted', score: earned, grade: earned === null ? null : String(earned),
    submitted_at: subDay !== null ? at(subDay, 15, 52) : (earned !== null ? at(dueDay - 1, 16, 1) : null), graded_at: earned !== null ? at(extra.gradedDay ?? dueDay, 8, 0) : null,
    // Canvas posts a grade separately from marking it; one assignment here is marked but held back
    posted_at: earned !== null ? (extra.held ? null : at(dueDay, 8, 5)) : null,
    late: !!extra.late, missing: false, excused: false, attempt: submitted ? 1 : null,
    // 4001 is the worked example for the submission sheet: two attempts, and comments filed against
    // each of them, so the thread shown has to be the selected attempt's rather than all of them
    submission_comments: id === '4001'
      ? [{ author_id: `t${courseId}`, author_name: c.teacher, created_at: at(dueDay - 2, 9, 0), attempt: 1, comment: 'This is only a first pass — attach the working before the deadline and I will mark it.' }, { author_id: `t${courseId}`, author_name: c.teacher, created_at: at(dueDay + 1, 9, 0), attempt: 2, comment: 'Nice work on the derivative questions. Watch the difference between an instantaneous reading and an interval total — that cost you question 2.' }, { author_id: '7', author_name: 'Sam Student', created_at: at(dueDay + 1, 10, 0), attempt: 2, comment: 'Thanks, I see it now.' }]
      : extra.rubric ? [] : id === '1001'
        ? [{ author_id: `t${courseId}`, author_name: c.teacher, created_at: at(dueDay + 1, 9, 0), comment: 'Nice work on the derivative questions. Watch the difference between an instantaneous reading and an interval total — that cost you question 2.' }, { author_id: '7', author_name: 'Sam Student', created_at: at(dueDay + 1, 10, 0), comment: 'Thanks, I see it now.' }]
        : (earned !== null && id === '1002' ? [{ author_id: `t${courseId}`, author_name: c.teacher, created_at: at(dueDay + 1, 9, 0), attempt: 2, comment: 'Check the domain restrictions in question 3 — the rest was solid.' }] : []),
    rubric_assessment: extra.rubric && earned !== null ? { c1: { points: 4, rating_id: 'r2', comments: 'Sign error in part b.' }, c2: { points: 4, rating_id: 'r3' } } : undefined,
    // quiz assignments: Canvas keeps each attempt's per-question grading in submission_history
    submission_history: extra.quiz ? (quizSubs.get(String(Number(id) + 8000)) || []).filter((s) => s.workflow_state === 'complete').map((s) => ({ attempt: s.attempt, score: s.score, submission_data: quizQuestionBank(s.quiz_id).map((q) => ({ question_id: q.id, correct: gradeQuestion(q, s.state[q.id]?.answer), points: gradeQuestion(q, s.state[q.id]?.answer) ? q.points_possible : 0, ...histFields(q, s.state[q.id]?.answer) })) }))
      // everything else keeps what was handed in, attempt by attempt
      : (submitted ? [
        { attempt: 1, submitted_at: at(dueDay - 3, 14, 20), submission_type: 'online_text_entry', score: null, late: false, body: `<p>First pass at <strong>${name}</strong>. I will attach the working before the deadline.</p>` },
        { attempt: 2, submitted_at: subDay !== null ? at(subDay, 15, 52) : at(dueDay - 1, 16, 1), submission_type: 'online_upload', score: earned, late: !!extra.late, attachments: [{ id: `f${id}`, display_name: `${name.replace(/[^\w]+/g, '-')}.pdf`, filename: `${name}.pdf`, 'content-type': 'application/pdf', size: 148231, url: `/files/f${id}/download?download_frd=1` }] },
      ] : undefined),
    submission_type: submitted ? (extra.quiz ? 'online_quiz' : 'online_upload') : null,
    attachments: submitted && !extra.quiz ? [{ id: `f${id}`, display_name: `${name.replace(/[^\w]+/g, '-')}.pdf`, filename: `${name}.pdf`, 'content-type': 'application/pdf', size: 148231, url: `/files/f${id}/download?download_frd=1` }] : undefined,
  };
  return {
    id, name, description: extra.description || `<p>Complete <strong>${name}</strong> as described in lecture. Show all work and submit a single PDF.</p><ul><li>Use the chain rule where appropriate.</li><li>Label each step.</li></ul>${extra.rubric ? '<p>See the rubric for how points are awarded.</p>' : ''}`,
    due_at: due, lock_at: extra.window ? at(dueDay, 23, 59) : null, unlock_at: extra.window ? at(dueDay - 7, 0, 0) : null, points_possible: possible, grading_type: 'points', published: true, html_url: `/courses/${courseId}/assignments/${id}`,
    submission_types: extra.quiz ? ['online_quiz'] : extra.tool ? ['external_tool'] : extra.types || ['online_upload', 'online_text_entry'], is_quiz_assignment: !!extra.quiz, quiz_id: extra.quiz ? String(Number(id) + 8000) : undefined,
    allowed_extensions: mockConfig.ext?.[id] || extra.ext || [], locked_for_user: false, // (POST /__mock/config {"ext": {"4002": ["pdf"]}} narrows an assignment's types for a test)
    quiz_access_code: extra.code || null, quiz_ip_filter: extra.ip || null, quiz_lockdown: !!extra.lockdown, quiz_survey: extra.survey || null, quiz_code_hidden: !!extra.codeHidden, // (the mock's own notes: what the quiz built from this is restricted by)
    external_tool_tag_attributes: extra.tool ? { url: extra.tool, new_tab: false, resource_link_id: 'rl1' } : undefined,
    discussion_topic: extra.discussion ? { id: extra.discussion, title: name, html_url: `/courses/${courseId}/discussion_topics/${extra.discussion}` } : undefined, // a graded discussion: the assignment behind a topic
    assignment_group_id: `g${courseId}-${Math.max(gIdx, 0)}`, omit_from_final_grade: !!extra.omit, allowed_attempts: extra.attempts ?? 2, rubric: extra.rubric ? rubric : undefined, rubric_settings: extra.rubric ? { title: 'Dis01 rubric' } : undefined,
    submission: apiSubmissions.get(id) || submission, // a submission made through the API replaces the seeded one
  };
}
function groupNames(courseId) {
  const out = {};
  for (const r of A[courseId] || []) out[r[2]] = true;
  return out;
}
function assignmentGroups(courseId) {
  const defs = GROUPS[courseId] || Object.keys(groupNames(courseId)).map((g) => [g, 0]);
  return defs.map(([name, weight], i) => ({ id: `g${courseId}-${i}`, name, position: i + 1, group_weight: weight, rules: {}, assignments: (A[courseId] || []).filter((r) => r[2] === name).map((r) => assignmentObj(courseId, r)) }));
}
const allAssignments = (courseId) => (A[courseId] || []).map((r) => assignmentObj(courseId, r));
const apiSubmissions = new Map(); // assignment id -> the submission made through the API

// ---- planner ------------------------------------------------------------------------------
const overrides = new Map();
const notes = []; // planner notes the student created through the API (mockup 12)
let noteSeq = 0;
function plannerItems() {
  const items = [];
  for (const n of notes) {
    items.push({ context_type: n.course_id ? 'Course' : null, course_id: n.course_id || null, context_name: n.course_id ? courses.find((c) => c.id === n.course_id)?.name || null : null, plannable_id: n.id, plannable_type: 'planner_note', plannable_date: n.todo_date, plannable: { id: n.id, title: n.title, todo_date: n.todo_date, details: n.details || '' }, planner_override: overrides.get(`planner_note:${n.id}`) || null, submissions: false, html_url: null });
  }
  for (const c of courses) {
    if (c.past) continue;
    for (const a of allAssignments(c.id)) {
      const due = new Date(a.due_at);
      if (due < new Date(now.getTime() - 7 * D) || due > new Date(now.getTime() + 21 * D)) continue;
      const key = `${a.is_quiz_assignment ? 'quiz' : 'assignment'}:${a.is_quiz_assignment ? a.quiz_id : a.id}`;
      items.push({
        context_type: 'Course', course_id: c.id, context_name: c.name, plannable_id: a.is_quiz_assignment ? a.quiz_id : a.id, plannable_type: a.is_quiz_assignment ? 'quiz' : 'assignment', plannable_date: a.due_at,
        plannable: { id: a.id, title: a.name, due_at: a.due_at, points_possible: a.points_possible }, planner_override: overrides.get(key) || null,
        submissions: { submitted: !!a.submission.submitted_at, graded: a.submission.workflow_state === 'graded', missing: false, late: a.submission.late, excused: false, needs_grading: false },
        html_url: a.html_url,
      });
    }
  }
  // graded discussion, ungraded discussion (to-do date), page and event
  items.push({ context_type: 'Course', course_id: '101', context_name: 'F26-MATH 021 20', plannable_id: '7001', plannable_type: 'discussion_topic', plannable_date: at(5, 23, 59), plannable: { id: '7001', title: 'Discussion Quiz for this week', assignment_id: '7001a', due_at: at(5, 23, 59), points_possible: 5 }, planner_override: overrides.get('discussion_topic:7001') || null, submissions: { submitted: false, graded: false }, html_url: '/courses/101/discussion_topics/7001' });
  items.push({ context_type: 'Course', course_id: '102', context_name: 'F26-PHYS 008 01', plannable_id: '7002', plannable_type: 'discussion_topic', plannable_date: at(1, 9, 0), plannable: { id: '7002', title: 'Intro thread: say hello (ungraded)', todo_date: at(1, 9, 0) }, planner_override: overrides.get('discussion_topic:7002') || null, submissions: false, html_url: '/courses/102/discussion_topics/7002' });
  items.push({ context_type: 'Course', course_id: '101', context_name: 'F26-MATH 021 20', plannable_id: 'p1', plannable_type: 'wiki_page', plannable_date: at(2, 8, 0), plannable: { id: 'p1', title: 'Read: Chapter 4 notes', todo_date: at(2, 8, 0) }, planner_override: null, submissions: false, html_url: '/courses/101/pages/chapter-4-notes' });
  items.push({ context_type: 'Course', course_id: '202', context_name: 'Placement Exam: Chemistry', plannable_id: 'e1', plannable_type: 'calendar_event', plannable_date: at(6, 23, 59), plannable: { id: 'e1', title: 'Chemistry placement closes', start_at: at(6, 23, 59) }, planner_override: null, submissions: false, html_url: '/calendar?event_id=e1' });
  return items.sort((x, y) => new Date(x.plannable_date) - new Date(y.plannable_date));
}

// ---- discussions, announcements, pages, files, quizzes, modules, people --------------------
const discussions = {
  101: [
    { id: '7001', title: 'Discussion Quiz for this week', posted_at: ago(2 * D), last_reply_at: ago(20 * H), unread_count: 1, discussion_subentry_count: 1, read_state: 'unread', assignment: { id: '7001a', points_possible: 5, due_at: at(5, 23, 59) }, lock_at: at(6, 23, 59), message: '<p>Post one question you still have about continuity, then reply to a classmate.</p>' },
    { id: '7003', title: 'Is there any discussion happening this week?', posted_at: ago(3 * D), last_reply_at: ago(18 * H), unread_count: 23, discussion_subentry_count: 23, read_state: 'unread', message: '<p>Is there a discussion section this week or is it cancelled?</p>' },
    { id: '7004', title: 'Discussion and Quiz', posted_at: ago(4 * D), last_reply_at: ago(2 * D), unread_count: 0, discussion_subentry_count: 1, read_state: 'read', message: '<p>Where do I find the quiz for Dis01?</p>' },
    { id: '7005', title: 'Knewton Alta Lecture Discrepancy (Continuity, Symmetry, and One to One Functions)', posted_at: ago(5 * D), last_reply_at: ago(3 * D), unread_count: 0, discussion_subentry_count: 1, read_state: 'read', message: '<p>The Knewton section on symmetry uses a different definition than lecture.</p>', pinned: false },
  ],
  102: [{ id: '7002', title: 'Intro thread: say hello (ungraded)', posted_at: ago(6 * D), last_reply_at: ago(D), unread_count: 4, discussion_subentry_count: 12, read_state: 'unread', todo_date: at(1, 9, 0), message: '<p>Say hello and tell us why you are taking physics.</p>' }],
};
const announcements = {
  101: [
    { id: '8001', title: 'Prerequisite Skills Test', posted_at: ago(6 * D), read_state: 'unread', author: { display_name: 'Yue Lei' }, message: '<p>Good morning everyone, the results from the Skills_Check test have been posted. Please review them before Friday.</p>' },
    { id: '8002', title: 'Awesome opportunity for first-year students', posted_at: ago(15 * D), read_state: 'unread', author: { display_name: 'Yue Lei' }, message: '<p>Good morning everyone, I just learned about a wonderful opportunity for first-year students interested in research.</p>' },
    { id: '8003', title: 'Important: University Store Inclusive ACCESS Instructions', posted_at: ago(15 * D + 2 * H), read_state: 'read', author: { display_name: 'Campus Store' }, message: '<p>Dear students, at your instructor\'s request this course is participating in Inclusive ACCESS.</p>' },
    { id: '8004', title: 'Welcome to MATH 021', posted_at: ago(18 * D), read_state: 'read', author: { display_name: 'Yue Lei' }, message: '<p>Please read the Course Syllabus before our first lecture.</p>' },
  ],
  104: [{ id: '8005', title: 'Field site sign-ups', posted_at: ago(D), read_state: 'unread', author: { display_name: 'Ana Ruiz' }, message: '<p>Sign up for a field site by Friday.</p>' }],
};
const topicFull = (courseId, id) => {
  const t = [...(discussions[courseId] || []), ...(announcements[courseId] || [])].find((x) => x.id === String(id));
  return t ? { ...t, html_url: `/courses/${courseId}/discussion_topics/${t.id}`, locked: false, require_initial_post: !!t.require_initial_post, attachments: [] } : null;
};
const entries = new Map();
const viewFor = (topicId) => ({
  participants: [{ id: '8', display_name: 'Alan Aguilar', avatar_image_url: null }, { id: '9', display_name: 'Wail Ahmed', avatar_image_url: null }, { id: '7', display_name: 'Sam Student', avatar_image_url: null }],
  view: [{ id: 'e1', user_id: '8', created_at: ago(20 * H), message: '<p>Factory work pulled children out of the home, which changed who raised them.</p>', replies: [{ id: 'e2', user_id: '9', created_at: ago(10 * H), message: '<p>Agreed, and schooling laws followed.</p>', replies: [] }] }, ...(entries.get(topicId) || [])],
});
const pages = {
  101: [
    { url: 'course-information', title: 'Course Information', front_page: true, created_at: ago(18 * D), updated_at: ago(15 * D), last_edited_by: { display_name: 'Yue Lei' }, body: '<p><strong>Ask any question:</strong> use the <a href="/courses/101/discussion_topics">Discussions</a> page — please do not use “Ask your instructor a question.”</p><p><strong>Please read</strong> the <a href="/courses/101/files/f1">Course Syllabus</a> in Files → Course Information for all course policies, structure, materials and exam dates. A tentative schedule of lecture topics is in <a href="/courses/101/files/f2">Lecture schedule</a>.</p><p>Slides and worksheets used in lectures and discussion sections are under <a href="/courses/101/files">Files</a>. The textbook is a free online book on OpenStax, <em>Calculus Volume 1</em>. See <a href="https://example.edu/store">Knewton Alta</a> for access.</p><div style=\"background-color: #f0f0f0; padding: 16px;\"><h2>Welcome</h2><p class=\"bcv-t-onlight\">Read the syllabus before Module 1.</p></div>' },
    { url: 'chapter-4-notes', title: 'Chapter 4 notes', front_page: false, created_at: ago(5 * D), updated_at: ago(2 * D), last_edited_by: { display_name: 'Yue Lei' }, body: '<h2>Continuity</h2><p>A function is continuous at a point when the limit equals the value.</p><ul><li>Removable discontinuity</li><li>Jump discontinuity</li></ul>' },
  ],
  103: [{ url: 'lab-safety', title: 'Lab safety', front_page: true, created_at: ago(20 * D), updated_at: ago(19 * D), body: '<p>Goggles on at all times.</p>' }],
};
const folders = {
  'r101': { id: 'r101', name: 'course files', full_name: 'course files', context_id: '101', parent_folder_id: null, updated_at: ago(15 * D) },
  'f101a': { id: 'f101a', name: 'Course Information', full_name: 'course files/Course Information', context_id: '101', parent_folder_id: 'r101', updated_at: ago(15 * D) },
  'f101b': { id: 'f101b', name: 'Discussion Worksheets', full_name: 'course files/Discussion Worksheets', context_id: '101', parent_folder_id: 'r101', updated_at: ago(3 * D) },
  'f101c': { id: 'f101c', name: 'Group Projects', full_name: 'course files/Group Projects', context_id: '101', parent_folder_id: 'r101', updated_at: ago(9 * D), created_at: ago(9 * D) },
};
const files = {
  r101: [
    { id: 'f2', display_name: 'math21-F26 planned lecture schedule.xlsx', filename: 'schedule.xlsx', 'content-type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet', size: 49152, updated_at: ago(15 * D), url: '/files/f2/download' },
    { id: 'f1', display_name: 'Course Syllabus.pdf', filename: 'syllabus.pdf', 'content-type': 'application/pdf', size: 217088, updated_at: ago(18 * D), url: '/files/f1/download' },
    { id: 'f5', display_name: 'Lecture 3 whiteboard.png', filename: 'whiteboard.png', 'content-type': 'image/png', size: 302011, updated_at: ago(12 * D), url: '/files/f5/download?download_frd=1&verifier=abc' },
    { id: 'f6', display_name: 'reading-list.txt', filename: 'reading-list.txt', 'content-type': 'text/plain', size: 412, updated_at: ago(11 * D), url: '/files/f6/download?download_frd=1&verifier=def' },
  ],
  f101a: [{ id: 'f3', display_name: 'Resources_Policy.pdf', filename: 'Resources_Policy.pdf', 'content-type': 'application/pdf', size: 130000, updated_at: ago(16 * D), url: '/files/f3/download' }],
  f101b: [{ id: 'f4', display_name: 'Dis01 worksheet.pdf', filename: 'dis01.pdf', 'content-type': 'application/pdf', size: 80000, updated_at: ago(3 * D), url: '/files/f4/download' }],
  f101c: [],
};
const quizzes = (courseId) => allAssignments(courseId).filter((a) => a.is_quiz_assignment).map((a, i) => ({ id: a.quiz_id, title: a.name, due_at: a.due_at, points_possible: a.points_possible, question_count: mockConfig.richQuestions ? 7 : 4, quiz_type: a.quiz_survey || (a.name === 'Skills_Check' ? 'practice_quiz' : 'assignment'), time_limit: 20, allowed_attempts: mockConfig.richQuestions ? 5 : 1, description: `<p>${a.name}: four questions on the pre-lecture reading.</p>`, html_url: `/courses/${courseId}/quizzes/${a.quiz_id}`, locked_for_user: false, assignment_id: a.id, one_question_at_a_time: a.name === 'Lec07-PreQuiz', cant_go_back: a.name === 'Lec07-PreQuiz', hide_results: null, show_correct_answers: true, shuffle_answers: false, has_access_code: !!a.quiz_access_code && !a.quiz_code_hidden, ip_filter: a.quiz_ip_filter || null, require_lockdown_browser: !!a.quiz_lockdown }));

// ---- quiz attempts (stateful, like Canvas's quiz submission API) --------------------------
// quiz 9001 was taken once: q1 right, q2 wrong (17.68 m), q3 right, q4 right = 13 of 16
const quizSubs = new Map([['9001', [{ id: 'qs1', quiz_id: '9001', attempt: 1, score: 13, kept_score: 13, started_at: at(-14, 15, 30), finished_at: at(-14, 15, 52), workflow_state: 'complete', validation_token: 'tok-1', state: { 90011: { answer: 900111 }, 90012: { answer: 900124 }, 90013: { answer: [900131, 900133] }, 90014: { answer: 3.15 }, 90017: { answer: '<p>Pros:&nbsp;</p>\n<ul>\n<li>Ability to work together</li>\n<li>Divide and conquer assignments/group work&nbsp;</li>\n<li>Ideate together &amp; create better ideas.&nbsp;</li>\n</ul>\n<p>Cons</p>\n<ul>\n<li>Unreliable group mates cause a more stressful workload</li>\n<li>Have to set times to meet up outside of class&nbsp;</li>\n</ul>' } } }]]]); // (the essay, the way Canvas's editor keeps one, counts only when the rich set is on)
const quizQuestionBank = (quizId) => {
  const mc = (n, text, opts, extra = {}) => ({ id: `${quizId}${n}`, position: n, question_name: `Question ${n}`, question_type: 'multiple_choice_question', question_text: `<p>${text}</p>`, points_possible: 4, answers: opts.map((t, i) => ({ id: Number(`${quizId}${n}${i + 1}`), text: t, html: '', weight: i === 0 ? 100 : 0 })), ...extra });
  return [
    // instructor feedback the way Canvas stores it: neutral (always), or one comment per outcome; equations are Canvas's own images
    mc(1, 'What is the velocity at t = 5?', ['-3.15 m/s', '-2 m/s', '0 m/s', '1.37 m/s', 'None of the above'], { neutral_comments_html: '<p><img class="equation_image" title="v(5)=\\frac{dx}{dt}=0" src="/equation_images/v(5)%3D%5Cfrac%7Bdx%7D%7Bdt%7D%3D0" alt="v(5)=\\frac{dx}{dt}=0"> The position curve is flat at t = 5, so the slope — and the velocity — is zero.</p>' }),
    mc(2, 'What is the displacement between t = 0 and t = 5?', ['-3.15 m', '-2 m', '0 m', '17.68 m', 'None of the above'], { correct_comments_html: '<p>Area under the velocity curve over the first five seconds.</p>', incorrect_comments_html: '<p>17.68 m is the position reading at t = 5, not the change. Displacement is the area under the velocity curve over the interval.</p>' }),
    // answers that are nothing but a formula: Canvas leaves answer_text empty and holds the equation
    // as its own image, with the LaTeX on the tag (question 3 of the real quiz this mirrors)
    { id: `${quizId}3`, position: 3, question_name: 'Question 3', question_type: 'multiple_answers_question', question_text: '<p>Which of these are vector quantities, such as <img class=\"equation_image\" title=\"\\vec{v}\" src=\"/equation_images/%5Cvec%7Bv%7D\" alt=\"LaTeX: \\vec{v}\" data-equation-content=\"\\vec{v}\">?</p>', points_possible: 4, answers: [{ id: Number(`${quizId}31`), text: '', html: '<p><img class=\"equation_image\" title=\"\\vec{v}\" src=\"/equation_images/%5Cvec%7Bv%7D\" alt=\"LaTeX: \\vec{v}\" data-equation-content=\"\\vec{v}\"></p>', weight: 100 }, { id: Number(`${quizId}32`), text: 'Speed', html: '', weight: 0 }, { id: Number(`${quizId}33`), text: '', html: '<p><img class=\"equation_image\" title=\"\\vec{a}=\\frac{d\\vec{v}}{dt}\" src=\"/equation_images/%5Cvec%7Ba%7D\" alt=\"LaTeX: \\vec{a}=\\frac{d\\vec{v}}{dt}\" data-equation-content=\"\\vec{a}=\\frac{d\\vec{v}}{dt}\"></p>', weight: 100 }] },
    // Canvas's own shapes for these: matching carries its right-hand list in `matches`, and the blank
    // kinds tag every answer with the blank it belongs to. Test-only, so every other count stands.
    ...(mockConfig.richQuestions ? [{ id: `${quizId}5`, position: 5, question_name: 'Question 5', question_type: 'matching_question', question_text: '<p>Match each reading to what it measures.</p>', points_possible: 3,
      answers: [
        { id: Number(`${quizId}51`), text: '9.8', match_id: 901 },
        { id: Number(`${quizId}52`), text: '3.0 × 10⁸', match_id: 902 },
        { id: Number(`${quizId}53`), text: '6.67 × 10⁻¹¹', match_id: 903 },
      ],
      matches: [{ match_id: 901, text: 'Acceleration due to gravity' }, { match_id: 902, text: 'Speed of light' }, { match_id: 903, text: 'Gravitational constant' }] },
    { id: `${quizId}6`, position: 6, question_name: 'Question 6', question_type: 'multiple_dropdowns_question', question_text: '<p>Velocity is the [rate] of [what] with respect to time.</p>', points_possible: 2,
      answers: [
        { id: Number(`${quizId}61`), text: 'rate of change', blank_id: 'rate', weight: 100 },
        { id: Number(`${quizId}62`), text: 'total amount', blank_id: 'rate', weight: 0 },
        { id: Number(`${quizId}63`), text: 'position', blank_id: 'what', weight: 100 },
        { id: Number(`${quizId}64`), text: 'mass', blank_id: 'what', weight: 0 },
      ] },
    // an essay: Canvas keeps the answer as the editor's HTML (paragraphs, lists), which is what comes back
    { id: `${quizId}7`, position: 7, question_name: 'Question 7', question_type: 'essay_question', question_text: '<p>What are the pros and cons of group work?</p>', points_possible: 2, answers: [] }] : []),
    { id: `${quizId}4`, position: 4, question_name: 'Question 4', question_type: 'numerical_question', question_text: '<p>At what time (in seconds) is the object momentarily at rest? See the <a href="/courses/101/pages/chapter-4-notes">chapter 4 notes</a>.</p>', points_possible: 5, answers: [{ id: Number(`${quizId}41`), text: '3.15', weight: 100, exact: 3.15 }], neutral_comments: 'Only one root in the interval: v(t) = 0 at t = 3.15 s.' },
  ];
};
const gradeQuestion = (q, a) => {
  if (a === null || a === undefined || a === '') return false;
  if (q.question_type === 'essay_question') return String(a).replace(/<[^>]+>/g, '').trim().length > 0; // (anything written earns the points here)
  if (q.question_type === 'numerical_question') return Number(a) === q.answers[0].exact;
  // matching: every left-hand value set against the match it was written with
  if (q.question_type === 'matching_question') {
    const want = new Map(q.answers.map((x) => [String(x.id), String(x.match_id)]));
    const got = new Map((Array.isArray(a) ? a : []).map((p2) => [String(p2.answer_id), String(p2.match_id)]));
    return want.size === got.size && [...want].every(([k, v]) => got.get(k) === v);
  }
  // a blank each: the option Canvas weights 100 for that blank, or the text it holds
  if (q.question_type === 'multiple_dropdowns_question' || q.question_type === 'fill_in_multiple_blanks_question') {
    const blanks = [...new Set(q.answers.map((x) => x.blank_id))];
    const held = a && typeof a === 'object' && !Array.isArray(a) ? a : {};
    return blanks.every((b) => {
      const right = q.answers.filter((x) => x.blank_id === b && x.weight === 100);
      return right.some((x) => String(held[b]) === String(q.question_type === 'multiple_dropdowns_question' ? x.id : x.text));
    });
  }
  const right = q.answers.filter((x) => x.weight === 100).map((x) => String(x.id)).sort();
  const picked = (Array.isArray(a) ? a : [a]).map(String).sort();
  return picked.join() === right.join();
};
const pubSub = ({ state, read, ...s }) => s;
// the student's answer the way Canvas's graded history records it (answer_id / answer_<id> flags / text)
const histFields = (q, a) => {
  if (a === null || a === undefined || a === '') return {};
  if (q.question_type === 'multiple_answers_question') return Object.fromEntries((Array.isArray(a) ? a : [a]).map((id) => [`answer_${id}`, '1']));
  if (q.question_type === 'numerical_question') return { text: String(a) };
  return { answer_id: a, text: String(a) };
};
const findSub = (id) => [...quizSubs.values()].flat().find((s) => s.id === id) || null;
// the access code an attempt's quiz wants, if any (the config can change it under a running attempt); refused the way Canvas refuses
const codeOf = (s) => (s && s.course_id ? (mockConfig.quizCode?.[s.quiz_id] || allAssignments(s.course_id).find((x) => x.quiz_id === s.quiz_id)?.quiz_access_code || null) : null);
const codeRefused = (s, body) => { const need = codeOf(s); return need && (body || {}).access_code !== need ? { __status: 403, errors: [{ message: 'invalid access code' }] } : null; };
const subQuestions = (s) => {
  const done = s.workflow_state === 'complete';
  const bank = quizQuestionBank(s.quiz_id);
  return {
    // like Canvas: `correct`, answer weights and the question comments only appear once the attempt is complete
    quiz_submission_questions: bank.map((q) => ({ id: q.id, position: q.position, flagged: !!s.state[q.id]?.flagged, answer: s.state[q.id]?.answer ?? null, ...(done ? { correct: gradeQuestion(q, s.state[q.id]?.answer) } : {}) })),
    quiz_questions: bank.map((q) => (done ? q : { ...q, neutral_comments_html: undefined, correct_comments_html: undefined, incorrect_comments_html: undefined, neutral_comments: undefined, answers: q.answers.map(({ weight, ...a }) => a) })),
  };
};
const modules = {
  102: [
    { id: 'm1', name: 'Week 1: Kinematics', state: 'completed', position: 1, items: [{ id: 'i1', type: 'Page', title: 'Big picture', html_url: '/courses/102/pages/big-picture', completion_requirement: { type: 'must_view', completed: true } }, { id: 'i2', type: 'Assignment', title: 'Lab 1 report', html_url: '/courses/102/assignments/2001', content_details: { due_at: at(-7, 23, 59), points_possible: 20 }, completion_requirement: { type: 'must_submit', completed: true } }] },
    { id: 'm2', name: 'Week 2: Forces', state: 'started', position: 2, items: [{ id: 'i3', type: 'SubHeader', title: 'Before class' }, { id: 'i4', type: 'Page', title: 'Newton’s laws', html_url: '/courses/102/pages/newtons-laws', indent: 1, completion_requirement: { type: 'must_view', completed: true } }, { id: 'i5', type: 'Assignment', title: 'W2 HW', html_url: '/courses/102/assignments/2002', indent: 1, content_details: { due_at: at(-1, 23, 59), points_possible: 15 }, completion_requirement: { type: 'must_submit', completed: false } }, { id: 'i6', type: 'ExternalUrl', title: 'PhET simulation', external_url: 'https://phet.colorado.edu', html_url: 'https://phet.colorado.edu' }] },
    { id: 'm3', name: 'Week 3: Energy', state: 'locked', position: 3, unlock_at: at(5, 8, 0), items: [] },
    // built last, due first: the one module whose course order and date order disagree
    { id: 'm4', name: 'Week 0: Orientation', state: 'completed', position: 4, items: [{ id: 'i7', type: 'Assignment', title: 'Safety quiz', html_url: '/courses/102/assignments/2001', content_details: { due_at: at(-21, 23, 59), points_possible: 5 } }] },
  ],
  101: [{ id: 'm11', name: 'Unit 1: Functions', state: 'started', position: 1, items: [{ id: 'i11', type: 'Page', title: 'Course Information', html_url: '/courses/101/pages/course-information' }, { id: 'i14', type: 'Page', page_url: 'chapter-4-notes', title: 'Chapter 4 notes', html_url: '/courses/101/pages/chapter-4-notes', completion_requirement: { type: 'must_mark_done', completed: false } },{ id: 'i12', type: 'Quiz', title: 'Lec06-PreQuiz', html_url: '/courses/101/quizzes/9011', content_details: { due_at: at(1, 10, 30), points_possible: 17 } },
    // an assignment with nothing to hand in: the module asks for a mark instead, as Canvas's "Mark as done"
    { id: 'i13', type: 'Assignment', content_id: '1003', title: 'Dis00', html_url: '/courses/101/assignments/1003', completion_requirement: { type: 'must_mark_done', completed: false } }] }],
  // a graded discussion sits in its module as the topic, not as its assignment: the sequence asked for
  // the assignment names nothing, as Canvas's does, and the item is found through the modules instead
  105: [{ id: 'm51', name: 'Journals', state: 'started', position: 1, items: [{ id: 'i51', type: 'Discussion', content_id: '7503', title: 'Journal #2', html_url: '/courses/105/discussion_topics/7503', completion_requirement: { type: 'must_mark_done', completed: false } }] }],
};
/** Canvas's module_item_sequence: the module item an asset is, and the items either side of it. */
function moduleItemSequence(courseId, assetType, assetId) {
  const flat = (modules[courseId] || []).flatMap((mod) => (mod.items || []).filter((it) => it.type !== 'SubHeader').map((it) => ({ ...it, module_id: mod.id })));
  const re = new RegExp(`/${{ Assignment: 'assignments', Quiz: 'quizzes', Page: 'pages', Discussion: 'discussion_topics', File: 'files' }[assetType] || assetType}/${assetId}$`);
  const i = flat.findIndex((it) => it.type === assetType && re.test(it.html_url || ''));
  if (i < 0) return { items: [], modules: [] };
  // test-only: a live Canvas can answer the sequence with the items bare of their completion requirement
  const bare = (it) => { if (!it || !mockConfig.bareSequence) return it || null; const { completion_requirement, ...rest } = it; return rest; };
  return { items: [{ prev: bare(flat[i - 1]), current: bare(flat[i]), next: bare(flat[i + 1]) }], modules: (modules[courseId] || []).map((m) => ({ id: m.id, name: m.name })) };
}
const setItemDone = (courseId, mid, iid, done) => {
  const it = (modules[courseId] || []).find((m) => m.id === mid)?.items.find((x) => x.id === iid);
  if (!it || it.completion_requirement?.type !== 'must_mark_done') return null;
  it.completion_requirement.completed = done;
  return { ...it, module_id: mid };
};
const people = (courseId) => [
  ['u1', 'Victor Adinna', null, 'Discussion-32D · Lecture-30', 'StudentEnrollment'], ['u2', 'Alan Aguilar', 'He/Him/His', 'Discussion-24D · Lecture-20', 'StudentEnrollment'], ['u3', 'Wail Ahmed', null, 'Discussion-22D · Lecture-20', 'StudentEnrollment'],
  ['u4', 'Alejandro Alarcon', 'He/Him/His', 'Discussion-32D · Lecture-30', 'StudentEnrollment'], ['u5', 'alyssa/landon alvarado', 'She/They', 'Discussion-33D · Lecture-30', 'StudentEnrollment'], ['u6', 'Salvador Alvarez Madriz', null, 'Discussion-21D · Lecture-20', 'StudentEnrollment'],
  [`t${courseId}`, courseById(courseId).teacher, null, 'Lecture-20 · Lecture-30', 'TeacherEnrollment'],
].map(([id, name, pronouns, secs, type]) => ({ id, name, sortable_name: name, pronouns, avatar_url: null, enrollments: secs.split(' · ').map((s, i) => ({ type, course_section_id: `sec-${courseId}-${s}`, enrollment_state: 'active', id: `${id}-${i}` })) }));
const sections = (courseId) => [...new Set(people(courseId).flatMap((u) => u.enrollments.map((e) => e.course_section_id)))].map((id) => ({ id, name: id.replace(`sec-${courseId}-`, '') }));

// ---- inbox ----------------------------------------------------------------------------------
const conversations = [
  { id: 'c1', subject: 'No submission for Acknowledge the UC Merced Student Attestation', workflow_state: 'unread', last_message: 'Hello Bobcat! You are receiving this message because our records show no submission yet.', last_message_at: ago(9 * D), starred: false, context_name: 'Student Rights & Responsibilities', context_code: 'course_201', participants: [{ id: '20', name: 'Halley Smith' }, { id: '7', name: 'Sam Student' }], messages: [{ id: 'm1', author_id: '20', created_at: ago(9 * D), body: 'Hello Bobcat!\n\nYou are receiving this message because our records show no submission for the Student Attestation. Please complete it by Friday.' }] },
  { id: 'c2', subject: 'Office hours this week', workflow_state: 'read', last_message: 'Office hours move to Thursday 2–4pm this week only.', last_message_at: ago(2 * D), starred: true, context_name: 'F26-MATH 021 20', context_code: 'course_101', participants: [{ id: 't101', name: 'Yue Lei' }, { id: '7', name: 'Sam Student' }], messages: [{ id: 'm2', author_id: 't101', created_at: ago(2 * D), body: 'Office hours move to Thursday 2–4pm this week only.' }] },
];

// ---- HTML pages -------------------------------------------------------------------------------
function page({ title, path = '', courseId, body }) {
  // like Canvas, the page's ENV carries the dashboard view the user last saved
  const env = { current_user_id: '7', current_user: { display_name: 'Sam Student', avatar_image_url: null }, COURSE_ID: courseId || null, context_asset_string: courseId ? `course_${courseId}` : 'user_7', TIMEZONE: 'America/Los_Angeles', DOMAIN_ROOT_ACCOUNT_ID: '1', PREFERENCES: { dashboard_view: dashboardView, custom_colors: Object.fromEntries(courses.map((c) => [`course_${c.id}`, c.color])) } };
  return `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>${title}</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="apple-touch-icon" href="data:image/svg+xml;utf8,${encodeURIComponent('<svg xmlns=\'http://www.w3.org/2000/svg\' viewBox=\'0 0 180 180\'><rect width=\'180\' height=\'180\' fill=\'#0b2b52\'/><path d=\'M30 140V40l60 60 60-60v100h-30V95l-30 30-30-30v45z\' fill=\'#f0b429\'/></svg>')}">
<link rel="icon" href="/dist/images/favicon-abc123.ico">
<script>INST = {"environment":"development"}; ENV = ${JSON.stringify(env)}; BRANDS = {};</script>
<style>
  :root{--ic-brand-global-nav-bgd:#0b2b52;--ic-brand-header-image:url("data:image/svg+xml;utf8,${encodeURIComponent('<svg xmlns=\'http://www.w3.org/2000/svg\' viewBox=\'0 0 40 40\'><path d=\'M6 32V8l14 14L34 8v24h-7V22l-7 7-7-7v10z\' fill=\'#f0b429\'/></svg>')}")}
  body{margin:0;font-family:-apple-system,BlinkMacSystemFont,sans-serif;color:#2d3b45;background:#fff}
  .ic-app-header__logomark{display:block;height:60px;background:var(--ic-brand-header-image) center/contain no-repeat}
  #header{position:fixed;left:0;top:0;bottom:0;width:84px;background:#394B58;color:#fff}
  #menu{list-style:none;margin:0;padding:0}.ic-app-header__menu-list-link{display:block;padding:12px 4px;color:#fff;text-decoration:none;font-size:11px;text-align:center}
  .ic-Layout-wrapper{margin-left:84px}#main{display:flex}#not_right_side{flex:1;padding:0 24px}#right-side-wrapper{width:300px;padding:24px}
  .btn{padding:6px 12px;border:1px solid #c7cdd1;background:#f5f5f5;border-radius:3px}
</style></head>
<body>
<div id="application" class="ic-app">
<header id="header" class="ic-app-header no-print"><div class="ic-app-header__logomark-container"><a href="/" class="ic-app-header__logomark"></a></div><ul id="menu"><li class="menu-item"><a id="global_nav_dashboard_link" href="/" class="ic-app-header__menu-list-link"><span class="menu-item__text">Dashboard</span></a></li><li class="menu-item"><a id="global_nav_courses_link" href="/courses" class="ic-app-header__menu-list-link"><span class="menu-item__text">Courses</span></a></li><li class="menu-item"><a id="global_nav_calendar_link" href="/calendar" class="ic-app-header__menu-list-link"><span class="menu-item__text">Calendar</span></a></li><li class="menu-item"><button id="global_nav_history_link" class="ic-app-header__menu-list-link" type="button"><span class="menu-item__text">History</span></button></li><li class="menu-item ic-app-header__menu-list-item"><a class="ic-app-header__menu-list-link" href="/accounts/1/external_tools/77?launch_type=global_navigation"><img class="ic-icon-svg ic-icon-svg--lti" src="/images/tool-icon.svg" alt=""><span class="menu-item__text">My Materials</span></a></li><li class="menu-item"><a id="global_nav_help_link" href="#" class="ic-app-header__menu-list-link"><span class="menu-item__text">Help</span></a></li></ul></header>
<div id="wrapper" class="ic-Layout-wrapper">
  <div id="main" class="ic-Layout-columns">
    <div id="not_right_side" class="ic-app-main-content"><div id="content" class="ic-Layout-contentMain">${body}</div></div>
    <aside id="right-side-wrapper"><div id="right-side"><h2>To Do</h2><p>Canvas's own sidebar</p></div></aside>
  </div>
  <footer id="footer">Canvas footer</footer>
</div></div></body></html>`;
}
const htmlPages = {
  '/': () => page({ title: 'Dashboard', body: '<h1 class="ic-Dashboard-header__title">Dashboard</h1><div id="dashboard">stock dashboard</div>' }),
  '/courses/101/external_tools/9': () => page({ title: 'Resources & Policy', courseId: '101', body: '<h2>Resources & Policy</h2><iframe id="tool_content" src="/courses/101/external_tools/retrieve?url=x" width="600" height="300" title="Tool"></iframe>' }),
  '/profile': () => page({ title: 'User Profile', body: '<h1>Sam Student</h1><p class="profile">Profile page rendered by Canvas.</p>' }),
  '/accounts/1/external_tools/77': () => page({ title: 'My Materials', body: '<h2 id="account-tool">My Materials (an account-level tool, launched by Canvas)</h2><iframe id="tool_content" title="My Materials" src="/courses/104/external_tools/t1/resource_selection"></iframe>' }),
  // a homework-submission tool's own picker, framed by the assignment page exactly as Canvas frames it;
  // when a file is chosen the return page posts externalContentReady to the window that framed it
  '/courses/104/external_tools/t1/resource_selection': () => `<!DOCTYPE html><html><head><meta charset="utf-8"><title>Box</title></head><body style="font-family:sans-serif;padding:24px"><h2 id="tool-title">Box picker (the tool's own page)</h2><p>The tool owns everything here. Choosing a file hands it back to the assignment page.</p><button id="pick" onclick="window.parent.postMessage({ subject: 'externalContentReady', service: 'external_tool_dialog', contents: [{ '@type': 'FileItem', url: 'http://localhost:${port}/files/box1/download', text: 'GC-articles-Sharma.pdf', mediaType: 'application/pdf' }] }, '*')">Use GC-articles-Sharma.pdf</button>
<p>Some tools want a window of their own.</p>
<button id="popout" onclick="window.bcvOpened = window.open('http://localhost:${port}/tool-window', '_blank')">Continue in a new window</button>
<a id="popout-link" href="http://localhost:${port}/tool-window" target="_blank">Or as a link</a></body></html>`,
  '/tool-window': () => `<!DOCTYPE html><html><head><meta charset="utf-8"><title>The tool's own window</title></head><body style="font-family:sans-serif;padding:24px"><h2 id="tool-window">The window the tool asked for</h2></body></html>`,
  '/courses/101/discussion_topics/new': () => page({ title: 'New Discussion Topic', courseId: '101', body: '<h1>New Discussion Topic</h1><form id="edit_discussion_form"><label for="discussion-title">Topic Title</label><input id="discussion-title" type="text" /><div class="tox-tinymce" role="application">Topic content</div><label><input type="checkbox" /> Participants must respond to the topic before viewing other replies</label><label><input type="checkbox" /> Allow liking</label><button type="submit" class="btn btn-primary">Save</button></form>' }),
  '/courses/101/quizzes/9011/take': () => page({ title: 'Lec06-PreQuiz', courseId: '101', body: '<h1>Lec06-PreQuiz</h1><form id="submit_quiz_form"><p>Question 1 of 4</p><label><input type="radio" name="q1"> A</label> <label><input type="radio" name="q1"> B</label><p><button type="button" class="btn">Submit Quiz</button></p></form>' }),
};

// ---- Canvas's own quiz-taking page -----------------------------------------------------------
// Rendered the way Canvas renders it (take_quiz.html.erb, the display_question / multi_answer /
// single_answer partials, the question list of the right column): the form with its classes and
// hidden fields, one question per page when the quiz says so, the Next/Previous buttons carrying
// the record_answer action, and — when going back is off — the first unread question whatever the
// URL asks for. The extension reads one-question-at-a-time quizzes from here, since the API refuses
// to list their questions.
const answeredQ = (s, q) => { const a = s.state[q.id]?.answer; if (a === null || a === undefined || a === '') return false; if (Array.isArray(a)) return !!a.length; if (typeof a === 'object') return !!Object.keys(a).length; return true; };
const takeQuestionHtml = (q, s) => {
  const st = s.state[q.id] || {};
  const a = st.answer;
  const label = (ans) => `<div class="answer_label" id="question_${q.id}_answer_${ans.id}_label">${ans.html || ans.text}</div>`;
  let answers;
  if (q.question_type === 'multiple_answers_question') answers = `<fieldset><legend class="screenreader-only">Group of answer choices</legend>${q.answers.map((ans) => `<div class="answer"><label class="answer_row user_content"><span class="answer_input"><input type="hidden" name="question_${q.id}_answer_${ans.id}" value="0"/><input type="checkbox" class="question_input" name="question_${q.id}_answer_${ans.id}" value="1" id="question_${q.id}_answer_${ans.id}"${(Array.isArray(a) ? a : []).map(String).includes(String(ans.id)) ? ' checked' : ''} aria-labelledby="question_${q.id}_answer_${ans.id}_label" /></span>${label(ans)}</label></div>`).join('')}</fieldset>`;
  else if (q.question_type === 'numerical_question') answers = `<div class="form-control numerical-question-holder"><input type="text" name="question_${q.id}" value="${a ?? ''}" class="form-control__input question_input numerical_question_input" autocomplete="off" aria-label="Numerical answer" /></div>`;
  else if (q.question_type === 'essay_question') answers = `<div class="form-control textarea-question-holder"><textarea name="question_${q.id}" class="question_input" autocomplete="off">${a ?? ''}</textarea></div>`;
  else if (q.question_type === 'short_answer_question') answers = `<div class="form-control text-box-question-holder"><input type="text" name="question_${q.id}" value="${a ?? ''}" class="question_input" autocomplete="off" /></div>`;
  else if (q.question_type === 'matching_question') answers = `<div class="answers_wrapper">${q.answers.map((ans) => {
    const on = (Array.isArray(a) ? a : []).find((p2) => String(p2.answer_id) === String(ans.id));
    return `<div class="answer"><div class="answer_match"><div class="answer_match_left">${ans.text}</div><div class="answer_match_right"><select class="question_input" name="question_${q.id}_answer_${ans.id}" aria-label="Match"><option value="">[ Choose ]</option>${(q.matches || []).map((m) => `<option value="${m.match_id}"${on && String(on.match_id) === String(m.match_id) ? ' selected' : ''}>${m.text}</option>`).join('')}</select></div></div></div>`;
  }).join('')}</div>`;
  else if (q.question_type === 'multiple_dropdowns_question' || q.question_type === 'fill_in_multiple_blanks_question') {
    const blanks = [...new Set(q.answers.map((ans) => ans.blank_id))];
    const held = a && typeof a === 'object' && !Array.isArray(a) ? a : {};
    answers = `<div class="answers_wrapper">${blanks.map((b) => (q.question_type === 'multiple_dropdowns_question'
      ? `<select class="question_input" name="question_${q.id}_${b}" aria-label="${b}"><option value="">[ Choose ]</option>${q.answers.filter((ans) => ans.blank_id === b).map((ans) => `<option value="${ans.id}"${String(held[b]) === String(ans.id) ? ' selected' : ''}>${ans.text}</option>`).join('')}</select>`
      : `<input type="text" class="question_input" name="question_${q.id}_${b}" value="${held[b] ?? ''}" aria-label="${b}" />`)).join('')}</div>`;
  }
  else answers = `<fieldset><legend class="screenreader-only">Group of answer choices</legend>${q.answers.map((ans) => `<div class="answer"><label class="answer_row user_content"><span class="answer_input"><input type="radio" class="question_input" name="question_${q.id}" value="${ans.id}" id="question_${q.id}_answer_${ans.id}"${String(a) === String(ans.id) ? ' checked' : ''} aria-labelledby="question_${q.id}_answer_${ans.id}_label" /></span>${label(ans)}</label></div>`).join('')}</fieldset>`;
  return `<div role="region" aria-label="Question" class="quiz_sortable question_holder"><div style="display: block; height: 1px; overflow: hidden;">&nbsp;</div><a name="question_${q.id}"></a><div class="display_question question ${q.question_type}${st.flagged ? ' marked' : ''}" id="question_${q.id}"><a href="#" class="flag_question" role="checkbox" aria-checked="${st.flagged ? 'true' : 'false'}"><span class="screenreader-only">Flag question: ${q.question_name}</span></a><div class="header"><span class="name question_name" role="heading" aria-level="2">${q.question_name}</span><span class="question_points_holder"><span class="points question_points">${q.points_possible}</span> pts</span></div><div style="display: none;"><span class="question_type">${q.question_type}</span><span class="answer_selection_type"></span></div><div class="text"><div class="original_question_text" style="display: none;"><textarea disabled style="display: none;" name="question_text" class="textarea_question_text">${q.question_text.replace(/</g, '&lt;')}</textarea></div><div id="question_${q.id}_question_text" class="question_text user_content">${q.question_text}</div><div class="answers">${answers}</div><div class="after_answers"></div></div><div class="clear"></div></div></div>`;
};
function takePage(courseId, quizId, questionId) {
  const q = quizzes(courseId).find((x) => x.id === quizId);
  const s = (quizSubs.get(quizId) || []).find((x) => x.workflow_state === 'untaken');
  if (!q || !s) return null;
  const bank = quizQuestionBank(quizId);
  s.read ||= {};
  let current = null;
  if (q.cant_go_back) current = bank.find((x) => !s.read[x.id]) || bank[bank.length - 1];
  else if (questionId) current = bank.find((x) => x.id === questionId) || null;
  else current = bank[0];
  if (!current) return null;
  const shown = q.one_question_at_a_time ? [current] : bank;
  const idx = bank.indexOf(current);
  const next = q.one_question_at_a_time ? bank[idx + 1] || null : null;
  const prev = q.one_question_at_a_time && !q.cant_go_back && idx > 0 ? bank[idx - 1] : null;
  const qPath = (x) => `/courses/${courseId}/quizzes/${quizId}/take/questions/${x.id}`;
  const action = (nextPath) => `/courses/${courseId}/quizzes/${quizId}/submissions/${s.id}/record_answer?user_id=7&next_question_path=${encodeURIComponent(nextPath)}`;
  const submitAction = `/courses/${courseId}/quizzes/${quizId}/submissions?user_id=7`;
  const classes = [q.one_question_at_a_time ? 'one_question_at_a_time' : 'all_questions', q.cant_go_back ? 'cant_go_back' : '', !next || !q.one_question_at_a_time ? 'last_page' : ''].filter(Boolean).join(' ');
  const icon = (x) => `<i class="placeholder ${answeredQ(s, x) ? 'icon-check' : 'icon-question'}"><span class="screenreader-only icon-text">${answeredQ(s, x) ? 'Answered' : "Haven't Answered Yet"}</span></i>`;
  const list = bank.map((x, i) => `<li id="list_question_${x.id}" class="list_question${answeredQ(s, x) ? ' answered' : ''}${s.state[x.id]?.flagged ? ' marked' : ''}${i <= idx ? ' seen' : ''}${q.one_question_at_a_time && x.id === current.id ? ' current_question' : ''}">${
    !q.one_question_at_a_time ? `<a class="jump_to_question_link" href="#question_${x.id}">${icon(x)}${x.question_name}<span class="screenreader-only marked-status"></span></a>`
      : q.cant_go_back ? `<span>${icon(x)}${x.question_name}<span class="screenreader-only marked-status"></span><span>` : `<a class="no-warning" href="${qPath(x)}">${icon(x)}${x.question_name}<span class="screenreader-only marked-status"></span></a>`}</li>`).join('');
  const body = `<h3 class="loading" style="display:none">Loading...</h3><div class="loaded"><header class="quiz-header"><h1>${q.title}</h1>Started: ${s.started_at}<h2>Quiz Instructions</h2><div id="quiz-instructions" class="user_content">${q.description}</div></header>
<form id="submit_quiz_form" class="${classes}" method="post" action="${next ? action(qPath(next)) : submitAction}"><div id="questions" class="assessing"><input type="hidden" name="attempt" value="${s.attempt}"/><input type="hidden" name="validation_token" value="${s.validation_token}"/><div style="display: none;" id="quiz_urls"><a href="/courses/${courseId}/quizzes/${quizId}/submissions/backup?user_id=7" class="backup_quiz_submission_url">&nbsp;</a><span class="started_at">${s.started_at}</span><span class="end_at">${s.end_at || ''}</span><span class="time_limit">${q.time_limit || ''}</span><span class="time_left">${s.end_at ? Math.round((new Date(s.end_at) - Date.now()) / 1000) : ''}</span></div>${shown.map((x) => takeQuestionHtml(x, s)).join('')}<div class="button-container clearfix">${prev ? `<button type="submit" class="Button submit_button previous-question" data-action="${action(qPath(prev))}" aria-label="Previous Question" disabled><i class="icon-mini-arrow-left"></i>Previous</button>` : ''}${next ? `<input type="hidden" name="last_question_id" id="last_question_id" value="${current.id}" /><button type="submit" class="Button submit_button  next-question" data-action="${action(qPath(next))}" aria-label="Next Question" disabled>Next<i class="icon-mini-arrow-right"></i></button>` : ''}</div></div><div class="form-actions"><span id="last_saved_indicator">Not saved</span><button type="submit" class="btn submit_button quiz_submit btn-secondary" id="submit_quiz_button" data-action="${submitAction}">Submit Quiz</button></div></form></div>`;
  const right = `<div><h3 style="margin: 0px;">Questions</h3><ul id="question_list" style="max-height: 200px; overflow: auto;" class="${q.cant_go_back ? 'read_only' : ''}">${list}</ul><div id="quiz-time-elapsed"><span class="time_header">Time Running:</span><div class="time_running"></div></div></div>`;
  return page({ title: q.title, courseId, body: `${body}<div id="right-side-wrapper"><aside id="right-side" role="complementary">${right}</aside></div>` });
}
// Next/Previous the way Canvas's page posts them: the question is marked read, any answers on the form are
// kept (unless going back is off and the question was already read), and the browser is sent on to the next page
function recordAnswer(courseId, quizId, subId, raw) {
  const q = quizzes(courseId).find((x) => x.id === quizId);
  const s = findSub(subId);
  const p = new URLSearchParams(raw);
  if (!q || !s || s.workflow_state !== 'untaken' || p.get('validation_token') !== s.validation_token) return `/courses/${courseId}/quizzes/${quizId}`;
  s.read ||= {};
  const bank = quizQuestionBank(quizId);
  for (const [k, v] of p) {
    const m = k.match(/^question_(\d+)(?:_answer_(\d+))?$/);
    if (!m) continue;
    if (q.cant_go_back && s.read[m[1]]) continue;
    const cur = s.state[m[1]] || (s.state[m[1]] = {});
    if (m[2]) {
      const set = new Set((Array.isArray(cur.answer) ? cur.answer : []).map(String));
      if (v === '1') set.add(m[2]); else set.delete(m[2]);
      cur.answer = [...set].map(Number);
    } else cur.answer = v === '' ? null : (bank.find((x) => x.id === m[1])?.question_type === 'numerical_question' && /^-?\d+(\.\d+)?$/.test(v) ? Number(v) : v);
  }
  const last = p.get('last_question_id');
  if (last) s.read[last] = true;
  return p.get('next_question_path') || `/courses/${courseId}/quizzes/${quizId}/take`;
}

// ---- API routing ------------------------------------------------------------------------------
const routes = [];
const on = (method, re, handler) => routes.push([method, re, handler]);
const json = (res, data, status = 200) => {
  // test-only: with cacheable on, API answers say they may be kept for ten minutes — the extension must not let the browser keep them
  const cache = mockConfig.cacheable && status === 200 ? { 'cache-control': 'public, max-age=600' } : {};
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8', ...cache });
  res.end('while(1);' + JSON.stringify(data));
};
const filterDates = (list, url, field) => {
  const s = url.searchParams.get('start_date'), e = url.searchParams.get('end_date');
  return list.filter((x) => (!s || new Date(x[field]) >= new Date(s)) && (!e || new Date(x[field]) <= new Date(e)));
};

on('GET', /^\/api\/v1\/users\/self$/, () => ({ id: '7', name: 'Sam Student', short_name: 'Sam', avatar_url: null }));
on('GET', /^\/api\/v1\/accounts\/1$/, () => ({ id: '1', name: 'Example University' }));
let dashboardView = 'planner';
on('GET', /^\/dashboard\/view$/, () => ({ dashboard_view: dashboardView }));
on('PUT', /^\/dashboard\/view$/, (url, m, body) => { dashboardView = body.dashboard_view || dashboardView; return { dashboard_view: dashboardView }; });
on('GET', /^\/api\/v1\/courses$/, () => courses.map(fullCourse));
on('PUT', /^\/api\/v1\/users\/self\/course_nicknames\/(\w+)$/, (url, m, body) => { const c = courseById(m[1]); if (!c) return null; const nick = String(url.searchParams.get('nickname') ?? body.nickname ?? '').trim(); if (nick) nicknames.set(m[1], nick); else nicknames.delete(m[1]); return { course_id: m[1], name: c.name, nickname: nick || null }; });
on('DELETE', /^\/api\/v1\/users\/self\/course_nicknames\/(\w+)$/, (url, m) => { nicknames.delete(m[1]); return { course_id: m[1], nickname: null }; });
on('GET', /^\/api\/v1\/dashboard\/dashboard_cards$/, () => courses.filter((c) => favorites.has(c.id)).map((c) => ({ id: c.id, shortName: nicknames.get(c.id) || c.name, originalName: c.name, courseCode: c.code, href: `/courses/${c.id}`, term: 'Fall 2026', subtitle: c.section, links: [{ css_class: 'announcements', label: 'Announcements', path: `/courses/${c.id}/announcements` }, { css_class: 'assignments', label: 'Assignments', path: `/courses/${c.id}/assignments` }, { css_class: 'discussions', label: 'Discussions', path: `/courses/${c.id}/discussion_topics` }, { css_class: 'files', label: 'Files', path: `/courses/${c.id}/files` }] })));
on('GET', /^\/api\/v1\/users\/self\/colors$/, () => ({ custom_colors: Object.fromEntries(courses.map((c) => [`course_${c.id}`, c.color])) }));
on('POST', /^\/api\/v1\/users\/self\/favorites\/courses\/(\w+)$/, (url, m) => { favorites.add(m[1]); return { context_id: m[1], context_type: 'Course' }; });
const hoursAgo = (h) => new Date(Date.now() - h * 3600e3).toISOString();
on('GET', /^\/api\/v1\/users\/self\/history$/, () => [
  { asset_name: 'Composition of Functions', asset_readable_category: 'Assignment', asset_icon: 'icon-assignment', context_name: 'F26-MATH 021 20', visited_url: `http://localhost:${port}/courses/101/assignments/1002`, visited_at: hoursAgo(2) },
  { asset_name: 'Lec06-PreQuiz', asset_readable_category: 'Quiz', asset_icon: 'icon-quiz', context_name: 'F26-MATH 021 20', visited_url: '/courses/101/quizzes/9011', visited_at: hoursAgo(5) },
  { asset_name: 'Week 2 Post Class Assignment: GC articles', asset_readable_category: 'Assignment', asset_icon: 'icon-assignment', context_name: 'F26-SPRK 010 103', visited_url: '/courses/104/assignments/4002', visited_at: hoursAgo(30) },
]);
on('GET', /^\/help_links$/, () => [
  { id: 'search_the_canvas_guides', text: 'Search the Canvas Guides', subtext: 'Find answers to common questions', url: 'https://community.canvaslms.com/t5/Canvas/ct-p/canvas', type: 'default', available_to: ['user', 'student'] },
  { id: 'report_a_problem', text: 'Report a Problem', subtext: 'If Canvas misbehaves, tell us about it', url: '#create_ticket', type: 'default', available_to: ['user', 'student'] },
  { id: 'it_help', text: 'IT Help Desk', subtext: 'Campus technology support', url: 'https://it.example.edu/help', type: 'custom', available_to: ['student'] },
]);
on('DELETE', /^\/api\/v1\/users\/self\/favorites\/courses\/(\w+)$/, (url, m) => { favorites.delete(m[1]); return { context_id: m[1] }; });
on('GET', /^\/api\/v1\/planner\/items$/, (url) => {
  // like Canvas: context_codes[] narrows the answer to those contexts; the student's own items
  // (a note without a course, a personal event) live under user_<id>
  const codes = url.searchParams.getAll('context_codes[]');
  const items = codes.length ? plannerItems().filter((it) => codes.includes(it.course_id ? `course_${it.course_id}` : 'user_7')) : plannerItems();
  return filterDates(items, url, 'plannable_date');
});
on('GET', /^\/api\/v1\/planner_notes$/, () => notes.slice());
on('POST', /^\/api\/v1\/planner_notes$/, (url, m, body) => { const n = { id: `note${++noteSeq}`, title: String(body.title || ''), todo_date: body.todo_date || null, course_id: body.course_id || null, details: body.details || '', workflow_state: 'active', user_id: 'self' }; notes.push(n); return n; });
on('DELETE', /^\/api\/v1\/planner_notes\/(\w+)$/, (url, m) => { const i = notes.findIndex((n) => n.id === m[1]); if (i < 0) return {}; const [n] = notes.splice(i, 1); return n; });
on('GET', /^\/api\/v1\/planner\/overrides$/, () => [...overrides.values()]);
on('POST', /^\/api\/v1\/planner\/overrides$/, (url, m, body) => { const ov = { id: `ov${overrides.size + 1}`, plannable_type: body.plannable_type, plannable_id: body.plannable_id, marked_complete: !!body.marked_complete, dismissed: !!body.dismissed }; overrides.set(`${body.plannable_type}:${body.plannable_id}`, ov); return ov; });
on('PUT', /^\/api\/v1\/planner\/overrides\/(\w+)$/, (url, m, body) => { for (const ov of overrides.values()) if (ov.id === m[1]) { Object.assign(ov, body); return ov; } return {}; });
on('GET', /^\/api\/v1\/users\/self\/activity_stream\/summary$/, () => [{ type: 'Announcement', unread_count: 3, count: 5 }, { type: 'Conversation', unread_count: 1, count: 2 }, { type: 'DiscussionTopic', unread_count: 4, count: 6 }]);
on('GET', /^\/api\/v1\/users\/self\/activity_stream$/, () => [
  { id: 'a1', type: 'Announcement', announcement_id: '8001', title: 'Prerequisite Skills Test', message: '<p>Good morning everyone, the results from the Skills_Check test have been posted…</p>', course_id: '101', context_type: 'Course', read_state: false, updated_at: ago(6 * D), html_url: '/courses/101/announcements/8001' },
  { id: 'a2', type: 'DiscussionTopic', discussion_topic_id: '7003', title: 'Is there any discussion happening this week?', message: '<p>Last post by Alan Aguilar.</p>', course_id: '101', context_type: 'Course', read_state: false, updated_at: ago(18 * H), total_root_discussion_entries: 23, html_url: '/courses/101/discussion_topics/7003' },
  { id: 'a3', type: 'Submission', title: 'Lec05-PreQuiz graded — 19 / 19', message: '<p>Effort group.</p>', course_id: '101', context_type: 'Course', read_state: true, updated_at: ago(D), html_url: '/courses/101/assignments/1007', assignment_id: '1007', score: 19, grade: '19', assignment: { id: '1007', name: 'Lec05-PreQuiz', points_possible: 19 } },
  { id: 'a6', type: 'Submission', title: 'Dis00', message: '', course_id: '101', context_type: 'Course', read_state: true, updated_at: ago(D + 2 * H), html_url: '/courses/101/assignments/1001', assignment_id: '1001', score: null, grade: null, assignment: { id: '1001', name: 'Dis00', points_possible: 10 }, submission_comments: [{ author_name: 'Joon', comment: 'Good use of interval notation here.', created_at: ago(D + 2 * H) }] },
  { id: 'a7', type: 'Message', title: 'Chemistry placement window closes Sep 16', message: '<p>One attempt remaining.</p>', notification_category: 'Due Date', course_id: '101', context_type: 'Course', read_state: true, updated_at: ago(3 * D), html_url: '/courses/101' },
  { id: 'a4', type: 'Conversation', title: 'No submission for Acknowledge the UC Merced Student Attestation', message: '<p>Hello Bobcat! You are receiving this message because our records show…</p>', conversation_id: 'c1', read_state: false, updated_at: ago(9 * D), html_url: '/conversations?id=c1' },
  { id: 'a5', type: 'Announcement', announcement_id: '8005', title: 'Field site sign-ups', message: '<p>Sign up for a field site by Friday.</p>', course_id: '104', context_type: 'Course', read_state: false, updated_at: ago(D), html_url: '/courses/104/announcements/8005' },
]);
// the Announcements API: every announcement across the given courses, with its read state
on('GET', /^\/api\/v1\/announcements$/, (url) => {
  const codes = url.searchParams.getAll('context_codes[]');
  const out = [];
  for (const [cid, list] of Object.entries(announcements)) if (codes.includes(`course_${cid}`)) for (const t of list) out.push({ ...t, context_code: `course_${cid}`, html_url: `/courses/${cid}/announcements/${t.id}` });
  return out.sort((a, b) => new Date(b.posted_at) - new Date(a.posted_at));
});
on('PUT', /^\/api\/v1\/users\/self\/colors\/course_(\w+)$/, (url, m, body) => { const c = courseById(m[1]); if (!c || !body.hexcode) return null; c.color = body.hexcode.startsWith('#') ? body.hexcode : `#${body.hexcode}`; return { hexcode: c.color }; });
on('GET', /^\/api\/v1\/conversations\/unread_count$/, () => ({ unread_count: String(conversations.filter((c) => c.workflow_state === 'unread').length) }));
on('GET', /^\/api\/v1\/conversations$/, (url) => { const scope = url.searchParams.get('scope'); const f = url.searchParams.getAll('filter[]')[0]; return conversations.filter((c) => (!scope || (scope === 'unread' ? c.workflow_state === 'unread' : scope === 'starred' ? c.starred : true)) && (!f || c.context_code === f)).map(({ messages, ...c }) => c); });
on('GET', /^\/api\/v1\/conversations\/(\w+)$/, (url, m) => conversations.find((c) => c.id === m[1]) || null);
on('PUT', /^\/api\/v1\/conversations\/(\w+)$/, (url, m, body) => { const c = conversations.find((x) => x.id === m[1]); if (c && body.conversation) Object.assign(c, body.conversation); return c; });
on('POST', /^\/api\/v1\/conversations\/(\w+)\/add_message$/, (url, m, body) => { const c = conversations.find((x) => x.id === m[1]); c.messages.unshift({ id: `m${Date.now()}`, author_id: '7', created_at: new Date().toISOString(), body: body.body }); c.last_message = body.body; c.last_message_at = new Date().toISOString(); return c; });
on('POST', /^\/api\/v1\/conversations$/, (url, m, body) => { const c = { id: `c${conversations.length + 1}`, subject: body.subject || '(no subject)', workflow_state: 'read', last_message: body.body, last_message_at: new Date().toISOString(), starred: false, context_code: body.context_code, participants: [{ id: '7', name: 'Sam Student' }, ...(body.recipients || []).map((r) => ({ id: r, name: `User ${r}` }))], messages: [{ id: 'mx', author_id: '7', created_at: new Date().toISOString(), body: body.body }] }; conversations.unshift(c); return [c]; });
on('GET', /^\/api\/v1\/search\/recipients$/, (url) => { const q = (url.searchParams.get('search') || '').toLowerCase(); return [{ id: 't101', name: 'Yue Lei', common_courses: {} }, { id: 'u2', name: 'Alan Aguilar' }, { id: 'course_101', name: 'F26-MATH 021 20', user_count: 120 }].filter((r) => r.name.toLowerCase().includes(q)); });
const groupList = [{ id: '66729', name: 'Attestation Fall 2026 1', course_id: '201', members_count: 4, group_category: { name: 'Attestation' }, description: '<p>Complete the student attestation with your group.</p>', context_type: 'Course' }, { id: '66730', name: 'Study group B', course_id: '301', members_count: 5, context_type: 'Course' }];
on('GET', /^\/api\/v1\/users\/self\/groups$/, () => (mockConfig.groupsFail ? { __status: 500, errors: [{ message: 'groups are having a moment' }] } : groupList));
on('GET', /^\/api\/v1\/groups\/(\w+)\/tabs$/, (url, m) => [['home', 'Home', ''], ['announcements', 'Announcements', '/announcements'], ['pages', 'Pages', '/pages'], ['people', 'People', '/users'], ['discussions', 'Discussions', '/discussion_topics'], ['files', 'Files', '/files'], ['conferences', 'BigBlueButton', '/conferences'], ['collaborations', 'Collaborations', '/collaborations']].map(([id, label, seg], i) => ({ id, label, html_url: `/groups/${m[1]}${seg}`, type: 'internal', position: i + 1, visibility: 'public' })));
on('GET', /^\/api\/v1\/groups\/(\w+)\/activity_stream$/, (url, m) => [{ id: 'ga1', type: 'Announcement', title: 'Attestation due Friday', message: '<p>Please complete it by Friday.</p>', read_state: false, updated_at: ago(2 * D), html_url: `/groups/${m[1]}/announcements/8801` }]);
on('GET', /^\/api\/v1\/groups\/(\w+)\/front_page$/, () => ({ errors: [{ message: 'No front page' }] }));
on('GET', /^\/api\/v1\/groups\/(\w+)\/discussion_topics$/, (url, m) => (url.searchParams.get('only_announcements') === 'true' ? [{ id: '8801', title: 'Attestation due Friday', posted_at: ago(2 * D), read_state: 'unread', author: { display_name: 'Halley Smith' }, message: '<p>Please complete it by Friday.</p>' }] : [{ id: '8802', title: 'Introductions', posted_at: ago(3 * D), last_reply_at: ago(D), unread_count: 2, discussion_subentry_count: 3, read_state: 'unread', message: '<p>Say hi to your group.</p>' }]).map((t) => ({ ...t, html_url: `/groups/${m[1]}/discussion_topics/${t.id}` })));
on('GET', /^\/api\/v1\/groups\/(\w+)\/discussion_topics\/(\w+)\/view$/, () => viewFor('none'));
on('GET', /^\/api\/v1\/groups\/(\w+)\/discussion_topics\/(\w+)$/, (url, m) => ({ id: m[2], title: m[2] === '8801' ? 'Attestation due Friday' : 'Introductions', posted_at: ago(2 * D), author: { display_name: 'Halley Smith' }, message: '<p>Say hi to your group.</p>', html_url: `/groups/${m[1]}/discussion_topics/${m[2]}`, locked: false, attachments: [] }));
on('GET', /^\/api\/v1\/groups\/(\w+)\/users$/, () => [{ id: 'u2', name: 'Alan Aguilar', sortable_name: 'Aguilar, Alan', avatar_url: null }, { id: '7', name: 'Sam Student', sortable_name: 'Student, Sam', avatar_url: null }]);
on('GET', /^\/api\/v1\/groups\/(\w+)\/pages$/, () => [{ url: 'group-notes', title: 'Group notes', front_page: false, created_at: ago(5 * D), updated_at: ago(D) }]);
on('GET', /^\/api\/v1\/groups\/(\w+)\/pages\/([^/]+)$/, () => ({ url: 'group-notes', title: 'Group notes', body: '<p>Meeting Tuesday.</p>', created_at: ago(5 * D), updated_at: ago(D) }));
on('GET', /^\/api\/v1\/groups\/(\w+)\/folders\/root$/, (url, m) => ({ id: `rg${m[1]}`, name: 'group files', full_name: 'group files', context_id: m[1] }));
on('GET', /^\/api\/v1\/groups\/(\w+)$/, (url, m) => { const g = groupList.find((x) => x.id === m[1]); return g ? { ...g, avatar_url: null } : null; });
// test-only switches: POST /__mock/config {"calendarFail": true} — cacheable: API answers carry a ten-minute max-age
const mockConfig = { calendarFail: false, cacheable: false, bareSequence: false };
on('POST', /^\/__mock\/config$/, (url, m, body) => Object.assign(mockConfig, body));
// test-only: the last API request as it arrived (its headers say whether the extension asked for a fresh answer)
let lastApi = null;
on('GET', /^\/__mock\/last-api$/, () => lastApi);
// test-only: a grade lands from elsewhere (a tool's frame, a teacher) — an assignment's score, a course's total
on('POST', /^\/__mock\/score$/, (url, m, body) => {
  if (body.assignmentId != null) scoreOverrides.set(String(body.assignmentId), body.score);
  if (body.courseId != null && body.courseScore != null) { const c = courseById(body.courseId); if (c) c.score = body.courseScore; }
  return { ok: true };
});
// test-only: put an attempt back to untaken, so Canvas's own take page has something to show
on('POST', /^\/__mock\/reopen-quiz$/, (url, m, body) => {
  const s = (quizSubs.get(String(body.quizId)) || [])[0];
  if (!s) return { __status: 404, errors: [{ message: 'no attempt' }] };
  s.workflow_state = 'untaken';
  return { ok: true };
});
on('GET', /^\/api\/v1\/calendar_events$/, (url) => {
  const codes = url.searchParams.getAll('context_codes[]');
  const type = url.searchParams.get('type') || 'event';
  if (mockConfig.calendarFail) return { __status: 500, errors: [{ message: 'calendar is having a moment' }] };
  // like Canvas: one off-limits context (a restricted course) refuses the whole request
  if (codes.includes('course_202')) return { __status: 401, errors: [{ message: 'user not authorized to perform that action' }] };
  let out = [];
  if (type === 'assignment') {
    for (const c of courses) if (codes.includes(`course_${c.id}`)) for (const a of allAssignments(c.id)) out.push({ id: `assignment_${a.id}`, title: a.name, start_at: a.due_at, end_at: a.due_at, all_day: false, context_code: `course_${c.id}`, context_name: c.name, type: 'assignment', html_url: a.html_url, assignment: { ...a, submission: a.submission } });
  } else {
    if (codes.includes('course_101')) { out.push({ id: 'ev1', title: 'Lec05 lecture', start_at: at(-1, 10, 30), end_at: at(-1, 11, 45), all_day: false, context_code: 'course_101', context_name: 'F26-MATH 021 20', type: 'event', html_url: '/calendar?event_id=ev1' }); out.push({ id: 'ev2', title: 'Midterm 1 review', start_at: at(13, 10, 30), end_at: at(13, 11, 45), all_day: false, context_code: 'course_101', context_name: 'F26-MATH 021 20', type: 'event', html_url: '/calendar?event_id=ev2' }); }
    if (codes.includes('course_102')) out.push({ id: 'ev3', title: 'Week 2 lab', start_at: at(-2, 10, 30), end_at: at(-2, 13, 0), all_day: false, context_code: 'course_102', context_name: 'F26-PHYS 008 01', type: 'event', html_url: '/calendar?event_id=ev3' });
    if (codes.includes('course_104')) out.push({ id: 'ev5', title: 'SPRK 010 seminar', start_at: at(0, 15, 0), end_at: at(0, 16, 15), all_day: false, context_code: 'course_104', context_name: 'F26-SPRK 010 103', type: 'event', html_url: '/calendar?event_id=ev5' }); // a class today: the dashboard's "Classes today" card
    if (codes.includes('course_202')) out.push({ id: 'e1', title: 'Chemistry placement closes', start_at: at(6, 0, 0), end_at: at(6, 23, 59), all_day: true, context_code: 'course_202', context_name: 'Placement Exam: Chemistry', type: 'event', html_url: '/calendar?event_id=e1' });
    if (codes.includes('user_7')) out.push({ id: 'ev4', title: 'Dentist', start_at: at(2, 15, 0), end_at: at(2, 16, 0), all_day: false, context_code: 'user_7', context_name: 'Sam Student', type: 'event', html_url: '/calendar?event_id=ev4' });
  }
  return filterDates(out, url, 'start_at');
});
on('GET', /^\/api\/v1\/courses\/(\w+)\/tabs$/, (url, m) => [['home', 'Home', ''], ['announcements', 'Announcements', '/announcements'], ['assignments', 'Assignments', '/assignments'], ['discussions', 'Discussions', '/discussion_topics'], ['grades', 'Grades', '/grades'], ['people', 'People', '/users'], ['pages', 'Pages', '/pages'], ['files', 'Files', '/files'], ['quizzes', 'Quizzes', '/quizzes'], ['modules', 'Modules', '/modules'], ['context_external_tool_9', 'Resources & Policy', '/external_tools/9']].map(([id, label, seg], i) => ({ id, label, html_url: `/courses/${m[1]}${seg}`, type: id.startsWith('context_external') ? 'external' : 'internal', position: i + 1, visibility: 'public' })));
on('GET', /^\/api\/v1\/courses\/(\w+)\/front_page$/, (url, m) => { const p = (pages[m[1]] || []).find((x) => x.front_page); return p ? { ...p, html_url: `/courses/${m[1]}/pages/${p.url}` } : { errors: [{ message: 'No front page' }] }; }, );
on('GET', /^\/api\/v1\/courses\/(\w+)\/todo$/, (url, m) => allAssignments(m[1]).filter((a) => !a.submission.submitted_at && new Date(a.due_at) > now).slice(0, 5).map((a) => ({ type: 'submitting', assignment: { id: a.id, name: a.name, due_at: a.due_at, html_url: a.html_url, points_possible: a.points_possible }, ignore: `/api/v1/users/self/todo/assignment_${a.id}/submitting?permanent=0`, ignore_permanently: `/api/v1/users/self/todo/assignment_${a.id}/submitting?permanent=1`, html_url: a.html_url, context_type: 'Course', course_id: m[1] })));
on('DELETE', /^\/api\/v1\/users\/self\/todo\//, () => ({ ok: true }));
on('GET', /^\/api\/v1\/courses\/(\w+)\/activity_stream$/, (url, m) => [{ id: 'ca1', type: 'Announcement', title: 'Prerequisite Skills Test', message: '<p>Results posted.</p>', course_id: m[1], read_state: false, updated_at: ago(6 * D), html_url: `/courses/${m[1]}/announcements/8001` }]);
// comments a student leaves on their own submission, kept for the life of the server
const ownComments = new Map();
on('GET', /^\/api\/v1\/courses\/(\w+)\/assignments\/(\w+)\/submissions\/self$/, (url, m) => {
  const sub = (allAssignments(m[1]).find((a) => a.id === m[2]) || {}).submission || null;
  const mine = ownComments.get(`${m[1]}:${m[2]}`) || [];
  return sub && mine.length ? { ...sub, submission_comments: [...(sub.submission_comments || []), ...mine] } : sub;
});
on('PUT', /^\/api\/v1\/courses\/(\w+)\/assignments\/(\w+)\/submissions\/self$/, (url, m, body) => {
  const text = body?.comment?.text_comment;
  if (!text) return { error: 'no comment' };
  const key = `${m[1]}:${m[2]}`;
  const list = ownComments.get(key) || [];
  list.push({ id: `oc${list.length + 1}`, author_id: '7', author_name: 'Sam Student', created_at: new Date().toISOString(), attempt: body.comment.attempt ? Number(body.comment.attempt) : undefined, comment: String(text) });
  ownComments.set(key, list);
  return (allAssignments(m[1]).find((a) => a.id === m[2]) || {}).submission || {};
});
on('GET', /^\/api\/v1\/courses\/(\w+)\/assignments\/(\w+)$/, (url, m) => allAssignments(m[1]).find((a) => a.id === m[2]) || null);
on('GET', /^\/api\/v1\/courses\/(\w+)\/assignments$/, (url, m) => allAssignments(m[1]));
on('GET', /^\/api\/v1\/courses\/(\w+)\/assignment_groups$/, (url, m) => assignmentGroups(m[1]));
on('GET', /^\/api\/v1\/courses\/(\w+)\/discussion_topics\/(\w+)\/view$/, (url, m) => viewFor(m[2]));
on('POST', /^\/api\/v1\/courses\/(\w+)\/discussion_topics\/(\w+)\/entries$/, (url, m, body) => { const e = { id: `e${Date.now()}`, user_id: '7', created_at: new Date().toISOString(), message: body.message, replies: [] }; entries.set(m[2], [...(entries.get(m[2]) || []), e]); return e; });
on('POST', /^\/api\/v1\/courses\/(\w+)\/discussion_topics\/(\w+)\/entries\/(\w+)\/replies$/, (url, m, body) => ({ id: `e${Date.now()}`, user_id: '7', created_at: new Date().toISOString(), message: body.message }));
on('PUT', /^\/api\/v1\/courses\/(\w+)\/discussion_topics\/(\w+)\/read_all$/, (url, m) => {
  for (const t of [...(announcements[m[1]] || []), ...(discussions[m[1]] || [])]) if (t.id === m[2]) { t.read_state = 'read'; t.unread_count = 0; }
  return { ok: true };
});
on('GET', /^\/api\/v1\/courses\/(\w+)\/discussion_topics\/(\w+)$/, (url, m) => topicFull(m[1], m[2]));
on('GET', /^\/api\/v1\/courses\/(\w+)\/discussion_topics$/, (url, m) => (url.searchParams.get('only_announcements') === 'true' ? (announcements[m[1]] || []) : (discussions[m[1]] || [])).map((t) => ({ ...t, html_url: `/courses/${m[1]}/discussion_topics/${t.id}` })));
on('GET', /^\/api\/v1\/courses\/(\w+)\/users$/, (url, m) => people(m[1]));
on('GET', /^\/api\/v1\/courses\/(\w+)\/sections$/, (url, m) => sections(m[1]));
on('GET', /^\/api\/v1\/courses\/(\w+)\/groups$/, (url, m) => (m[1] === '101' ? [{ id: 'cg1', name: 'Project group 3', members_count: 4, group_category: { name: 'project01' } }] : []));
on('GET', /^\/api\/v1\/courses\/(\w+)\/pages\/([^/]+)$/, (url, m) => (pages[m[1]] || []).find((p) => p.url === decodeURIComponent(m[2])) || null);
on('GET', /^\/api\/v1\/courses\/(\w+)\/pages$/, (url, m) => (pages[m[1]] || []).map(({ body, ...p }) => p));
on('GET', /^\/api\/v1\/courses\/(\w+)\/folders\/root$/, (url, m) => folders[`r${m[1]}`] || { id: `r${m[1]}`, name: 'course files', full_name: 'course files', context_id: m[1] });
on('GET', /^\/api\/v1\/courses\/(\w+)\/folders\/by_path\/(.+)$/, (url, m) => { const path = `course files/${decodeURIComponent(m[2])}`; const f = Object.values(folders).find((x) => x.full_name === path); return f ? [folders[`r${m[1]}`], f] : { errors: [{ message: 'not found' }] }; });
on('GET', /^\/api\/v1\/folders\/(\w+)\/folders$/, (url, m) => Object.values(folders).filter((f) => f.parent_folder_id === m[1]));
on('GET', /^\/api\/v1\/folders\/(\w+)\/files$/, (url, m) => files[m[1]] || []);
// one file by id, as the File API gives it (folder_id, and a preview_url only where Canvadocs would provide one: none here)
on('GET', /^\/api\/v1\/files\/(\w+)$/, (url, m) => { for (const [fid, list] of Object.entries(files)) { const f = list.find((x) => x.id === m[1]); if (f) return { ...f, folder_id: fid, preview_url: null, mime_class: (f['content-type'] || '').split('/')[0] }; } return { __status: 404, errors: [{ message: 'not found' }] }; });
on('GET', /^\/api\/v1\/courses\/(\w+)\/quizzes\/(\w+)\/submissions$/, (url, m) => ({ quiz_submissions: (quizSubs.get(m[2]) || []).map(pubSub) }));
on('POST', /^\/api\/v1\/courses\/(\w+)\/quizzes\/(\w+)\/submissions$/, (url, m, body) => {
  const list = quizSubs.get(m[2]) || [];
  const q = quizzes(m[1]).find((x) => x.id === m[2]);
  if (!q) return null;
  // a quiz with an access code refuses an attempt without it, the way Canvas does (403, "invalid access code")
  const asg = allAssignments(m[1]).find((x) => x.quiz_id === m[2]);
  if (asg?.quiz_access_code && (body || {}).access_code !== asg.quiz_access_code) return { __status: 403, errors: [{ message: 'invalid access code' }] };
  const s = { id: `qs${m[2]}-${list.length + 1}`, quiz_id: m[2], course_id: m[1], read: {}, user_id: '7', attempt: list.length + 1, started_at: new Date().toISOString(), end_at: q.time_limit ? new Date(Date.now() + q.time_limit * 60e3).toISOString() : null, finished_at: null, workflow_state: 'untaken', validation_token: `tok-${m[2]}-${list.length + 1}`, score: null, kept_score: null, state: {} };
  list.push(s);
  quizSubs.set(m[2], list);
  return { quiz_submissions: [pubSub(s)] };
});
// Canvas refuses the questions of a one-question-at-a-time quiz (its own page shows them), exactly as the real API does
const oneAtATime = (s) => !!(s.course_id && quizzes(s.course_id).find((q) => q.id === s.quiz_id)?.one_question_at_a_time);
on('GET', /^\/api\/v1\/quiz_submissions\/([\w-]+)\/questions$/, (url, m) => {
  const s = findSub(m[1]);
  if (!s) return null;
  if (oneAtATime(s)) return { __status: 400, errors: [{ message: 'Cannot receive one question at a time questions in the API' }] };
  return subQuestions(s);
});
on('POST', /^\/api\/v1\/quiz_submissions\/([\w-]+)\/questions$/, (url, m, body) => {
  const s = findSub(m[1]);
  if (!s || body.validation_token !== s.validation_token) return null;
  const refused = codeRefused(s, body);
  if (refused) return refused;
  for (const q of body.quiz_questions || []) s.state[String(q.id)] = { ...(s.state[String(q.id)] || {}), answer: q.answer };
  return subQuestions(s);
});
on('PUT', /^\/api\/v1\/quiz_submissions\/([\w-]+)\/questions\/(\w+)\/(flag|unflag)$/, (url, m, body) => { const s = findSub(m[1]); if (!s) return null; const refused = codeRefused(s, body); if (refused) return refused; s.state[m[2]] = { ...(s.state[m[2]] || {}), flagged: m[3] === 'flag' }; return subQuestions(s); });
on('GET', /^\/api\/v1\/courses\/(\w+)\/quizzes\/(\w+)\/submissions\/([\w-]+)\/time$/, (url, m) => { const s = findSub(m[3]); return s ? { end_at: s.end_at, time_left: s.end_at ? Math.round((new Date(s.end_at) - Date.now()) / 1000) : null } : null; });
on('POST', /^\/api\/v1\/courses\/(\w+)\/quizzes\/(\w+)\/submissions\/([\w-]+)\/complete$/, (url, m) => {
  const s = findSub(m[3]);
  if (!s) return null;
  s.workflow_state = 'complete';
  s.finished_at = new Date().toISOString();
  s.score = quizQuestionBank(s.quiz_id).reduce((sum, q) => sum + (gradeQuestion(q, s.state[q.id]?.answer) ? q.points_possible : 0), 0);
  s.kept_score = s.score;
  return { quiz_submissions: [pubSub(s)] };
});
// the quiz's question set as one attempt saw it (Quiz Questions API): a student reads only a finished attempt whose results are visible
on('GET', /^\/api\/v1\/courses\/(\w+)\/quizzes\/(\w+)\/questions$/, (url, m) => {
  const s = url.searchParams.get('quiz_submission_id') ? findSub(url.searchParams.get('quiz_submission_id')) : null;
  if (!s) return { __status: 401, errors: [{ message: 'user not authorized to perform that action' }] };
  if (s.workflow_state !== 'complete') return { __status: 401, errors: [{ message: 'Cannot view questions due to quiz settings' }] };
  return quizQuestionBank(m[2]);
});
// test-only: what the mock holds for an attempt (its read marks, answers and flags)
on('GET', /^\/__mock\/quizsub\/([\w-]+)$/, (url, m) => { const s = findSub(m[1]); return s ? { read: s.read || {}, answers: Object.fromEntries(Object.entries(s.state).map(([k, v]) => [k, v.answer ?? null])), flags: Object.fromEntries(Object.entries(s.state).map(([k, v]) => [k, !!v.flagged])) } : null; });
on('GET', /^\/api\/v1\/courses\/(\w+)\/quizzes\/(\w+)$/, (url, m) => quizzes(m[1]).find((q) => q.id === m[2]) || null);
on('GET', /^\/api\/v1\/courses\/(\w+)\/quizzes$/, (url, m) => quizzes(m[1]));
on('GET', /^\/api\/v1\/courses\/(\w+)\/modules$/, (url, m) => modules[m[1]] || []);
on('GET', /^\/api\/v1\/courses\/(\w+)\/module_item_sequence$/, (url, m) => moduleItemSequence(m[1], url.searchParams.get('asset_type'), url.searchParams.get('asset_id')));
on('PUT', /^\/api\/v1\/courses\/(\w+)\/modules\/(\w+)\/items\/(\w+)\/done$/, (url, m) => setItemDone(m[1], m[2], m[3], true));
on('DELETE', /^\/api\/v1\/courses\/(\w+)\/modules\/(\w+)\/items\/(\w+)\/done$/, (url, m) => setItemDone(m[1], m[2], m[3], false));

// ---- handing work in ------------------------------------------------------------------------
const pendingUploads = new Map(); // upload token -> preflight
const storedFiles = new Map(); // file id -> file json
const progresses = new Map();
let fileSeq = 0;
const homeworkTools = {
  104: [
    { id: 't1', name: 'Box', description: 'Pick a file from your Box drive', homework_submission: { enabled: true, text: 'Box', url: 'https://box.example.com/lti' } },
    { id: 't2', name: 'Office 365', description: 'Attach a Word or PowerPoint file', homework_submission: { enabled: true, text: 'Office 365', url: 'https://o365.example.com/lti' } },
  ],
};
on('GET', /^\/api\/v1\/courses\/(\w+)\/external_tools$/, (url, m) => (url.searchParams.get('placement') === 'homework_submission' ? homeworkTools[m[1]] || [] : []));
on('POST', /^\/api\/v1\/courses\/(\w+)\/assignments\/(\w+)\/submissions\/self\/files$/, (url, m, body) => {
  const a = allAssignments(m[1]).find((x) => x.id === m[2]);
  if (!a) return null;
  if (body.url) { // upload via URL: Canvas fetches the file itself and reports through a Progress
    const id = `uf${++fileSeq}`;
    storedFiles.set(id, { id, display_name: body.name || 'file', filename: body.name || 'file', size: 421888, 'content-type': body.content_type || 'application/pdf', url: `/files/${id}/download`, from_url: body.url });
    progresses.set(`p${id}`, { id: `p${id}`, workflow_state: 'completed', tag: 'upload_via_url', results: { id } });
    return { progress: { id: `p${id}`, workflow_state: 'queued', tag: 'upload_via_url' } };
  }
  const token = `tok${++fileSeq}`;
  pendingUploads.set(token, { name: body.name, size: body.size, content_type: body.content_type });
  return { upload_url: `http://localhost:${port}/__upload/${token}`, upload_params: { key: `submissions/${token}`, acl: 'private', success_action_status: '201' }, file_param: 'file' };
});
// the storage step: a multipart POST with no CSRF token, like S3 or inst-fs; the file is the last field
on('POST', /^\/__upload\/(\w+)$/, (url, m, body, raw) => {
  const pre = pendingUploads.get(m[1]);
  if (!pre) return null;
  const boundary = raw.slice(0, raw.indexOf('\r\n'));
  const part = raw.split(boundary).find((p) => /filename="/.test(p)) || '';
  const fname = (part.match(/filename="([^"]*)"/) || [])[1] || pre.name;
  const bytes = part.slice(part.indexOf('\r\n\r\n') + 4).replace(/\r\n$/, '');
  const id = `uf${++fileSeq}`;
  storedFiles.set(id, { id, display_name: fname, filename: fname, size: bytes.length, 'content-type': pre.content_type || 'application/octet-stream', url: `/files/${id}/download` });
  pendingUploads.delete(m[1]);
  return storedFiles.get(id);
});
on('GET', /^\/api\/v1\/progress\/(\w+)$/, (url, m) => progresses.get(m[1]) || null);
on('POST', /^\/api\/v1\/courses\/(\w+)\/assignments\/(\w+)\/submissions$/, (url, m, body) => {
  const a = allAssignments(m[1]).find((x) => x.id === m[2]);
  if (!a) return null;
  const s = body.submission || {};
  const type = s.submission_type === 'basic_lti_launch' ? 'online_url' : s.submission_type;
  if (!a.submission_types.includes(type)) return { __status: 400, errors: [{ message: `this assignment does not accept ${s.submission_type}` }] };
  if (type === 'online_upload' && (!Array.isArray(s.file_ids) || !s.file_ids.length || !s.file_ids.every((id) => storedFiles.has(String(id))))) return { __status: 400, errors: [{ message: 'no such file' }] };
  if (type === 'online_upload' && a.allowed_extensions.length && !s.file_ids.every((id) => a.allowed_extensions.includes(String(storedFiles.get(String(id)).display_name).split('.').pop().toLowerCase()))) return { __status: 400, errors: [{ message: 'file type not allowed' }] };
  if (type === 'online_text_entry' && !s.body) return { __status: 400, errors: [{ message: 'body is required' }] };
  if (type === 'online_url' && !s.url) return { __status: 400, errors: [{ message: 'url is required' }] };
  if (a.allowed_attempts > 0 && (a.submission.attempt || 0) >= a.allowed_attempts) return { __status: 400, errors: [{ message: 'no attempts left' }] };
  const when = new Date();
  const sub = {
    ...a.submission, workflow_state: 'submitted', submitted_at: when.toISOString(), attempt: (a.submission.attempt || 0) + 1, submission_type: s.submission_type,
    body: s.body || null, url: s.url || null, attachments: (s.file_ids || []).map((id) => storedFiles.get(String(id))), late: when > new Date(a.due_at), score: null, grade: null, graded_at: null,
    submission_comments: [...(a.submission.submission_comments || []), ...(body.comment?.text_comment ? [{ author_name: 'Sam Student', created_at: when.toISOString(), comment: body.comment.text_comment }] : [])],
  };
  apiSubmissions.set(a.id, sub);
  return sub;
});
on('GET', /^\/api\/v1\/courses\/(\w+)$/, (url, m) => { const c = courseById(m[1]); return c ? { ...fullCourse(c), syllabus_body: '<h2>Syllabus</h2><p>Lectures MWF 10:30. Midterm 1 in week 5, Midterm 2 in week 9, final in finals week. Late work loses 10% per day.</p>' } : null; });

// ---- a stand-in for CloudConvert (api.cloudconvert.com/v2), for the converter's cloud engine ---
// The key `cc-test-key` is the one that works. A job is one poll of "processing", then finished;
// a file whose name says "bad" fails the way the service fails one. /cc/__log lists what came up.
const CC_KEY = 'cc-test-key';
const ccJobs = new Map();
const ccLog = [];
const TINY_PDF = Buffer.from('%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n');
const ccJson = (res, data, status = 200) => { res.writeHead(status, { 'content-type': 'application/json' }); res.end(JSON.stringify(data)); }; // (plain JSON: no Canvas prefix on another host's answers)
function ccMock(req, res, path, raw) {
  const ok = (req.headers.authorization || '') === `Bearer ${CC_KEY}`;
  let m;
  if (path === '/cc/__log') return ccJson(res, ccLog);
  if (req.method === 'POST' && (m = path.match(/^\/cc\/upload\/(\w+)$/))) {
    const job = ccJobs.get(m[1]);
    if (!job) return ccJson(res, { message: 'No such upload.' }, 404);
    job.uploaded = raw.length;
    job.name = (raw.match(/filename="([^"]+)"/) || [])[1] || 'file';
    ccLog.push({ job: job.id, uploaded: raw.length, name: job.name, to: job.to });
    res.writeHead(201, { 'content-type': 'application/json' });
    return res.end('{}');
  }
  if (req.method === 'GET' && (m = path.match(/^\/cc\/files\/(\w+)\/(.+)$/))) {
    const job = ccJobs.get(m[1]);
    if (!job) return ccJson(res, { message: 'Gone.' }, 404);
    const isPdf = /\.pdf$/i.test(m[2]);
    const bytes = isPdf ? TINY_PDF : Buffer.from(`converted ${job.name} to ${job.to}\n`.repeat(40));
    res.writeHead(200, { 'content-type': isPdf ? 'application/pdf' : 'application/octet-stream', 'content-disposition': `attachment; filename="${m[2]}"`, 'content-length': bytes.length });
    return res.end(bytes);
  }
  if (!ok) return ccJson(res, { message: 'Unauthenticated.' }, 401);
  if (req.method === 'GET' && path === '/cc/v2/users/me') return ccJson(res, { data: { id: 1, username: 'sam', email: 'sam@example.edu', credits: 25 } });
  if (req.method === 'POST' && path === '/cc/v2/jobs') {
    let body = {};
    try { body = JSON.parse(raw); } catch { body = {}; }
    const conv = Object.values(body.tasks || {}).find((t) => t && t.operation === 'convert');
    const id = `job${ccJobs.size + 1}`;
    ccJobs.set(id, { id, to: conv?.output_format || 'pdf', polls: 0, uploaded: 0, name: '' });
    return ccJson(res, { data: { id, status: 'waiting', tasks: [{ id: `${id}-import`, name: 'import-1', operation: 'import/upload', status: 'waiting', result: { form: { url: `http://localhost:${port}/cc/upload/${id}`, parameters: { key: `up/${id}`, signature: 'x' } } } }] } }, 201);
  }
  if (req.method === 'GET' && (m = path.match(/^\/cc\/v2\/jobs\/(\w+)$/))) {
    const job = ccJobs.get(m[1]);
    if (!job) return ccJson(res, { message: 'Not found.' }, 404);
    job.polls++;
    if (job.polls < 2) return ccJson(res, { data: { id: job.id, status: 'processing', tasks: [] } });
    if (/bad/i.test(job.name)) return ccJson(res, { data: { id: job.id, status: 'error', tasks: [{ name: 'convert-1', operation: 'convert', status: 'error', code: 'INPUT_FILE_INVALID', message: 'The file could not be converted.' }] } });
    const out = `${job.name.replace(/\.[^.]+$/, '')}.${job.to}`;
    return ccJson(res, { data: { id: job.id, status: 'finished', tasks: [{ name: 'convert-1', operation: 'convert', status: 'finished' }, { name: 'export-1', operation: 'export/url', status: 'finished', result: { files: [{ filename: out, size: 1234, url: `http://localhost:${port}/cc/files/${job.id}/${out}` }] } }] } });
  }
  return ccJson(res, { message: 'Not found.' }, 404);
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url, `http://localhost:${port}`);
  const path = url.pathname.replace(/\/+$/, '') || '/';
  let raw = '';
  req.on('data', (c) => (raw += c));
  req.on('end', () => {
    let body = {};
    try {
      body = raw ? JSON.parse(raw) : {};
    } catch {
      body = {};
    }
    if (path.startsWith('/cc/')) return ccMock(req, res, path, raw); // (the conversion service, another host altogether)
    // like Canvas: every write needs the session's CSRF token, body or not (file storage is a separate
    // service and has none). The token lives in the _csrf_token cookie (URL-encoded), never in a meta tag.
    if (req.method !== 'GET' && !path.startsWith('/__mock/') && !path.startsWith('/__upload/') && path !== '/logout' && !path.endsWith('/record_answer') && req.headers['x-csrf-token'] !== CSRF) return json(res, { errors: [{ message: 'invalid authenticity token' }] }, 422);
    // a session that has ended: like Canvas, every API call answers 401 "unauthenticated" (the pages themselves are still served here, so the app boots and finds out)
    if (mockConfig.sessionLost && path.startsWith('/api/')) return json(res, { status: 'unauthenticated', errors: [{ message: 'user authorization required' }] }, 401);
    if (path.startsWith('/api/')) lastApi = { method: req.method, path, headers: req.headers };
    for (const [method, re, handler] of routes) {
      if (method !== req.method) continue;
      const m = path.match(re);
      if (!m) continue;
      const data = handler(url, m, body, raw);
      if (data === null) return json(res, { errors: [{ message: 'not found' }] }, 404);
      if (data && data.__status) return json(res, { errors: data.errors || [] }, data.__status);
      return json(res, data);
    }
    if (path.startsWith('/api/') || path === '/dashboard/view') return json(res, { errors: [{ message: 'not found' }] }, 404);
    if (path.startsWith('/courses/101/external_tools/retrieve')) {
      res.writeHead(200, { 'content-type': 'text/html' });
      return res.end('<html><body style="font-family:sans-serif;padding:20px">Embedded tool content</body></html>');
    }
    if (path.startsWith('/files/') && !path.endsWith('/file_preview')) { // a file's bytes, as its content type says (an attachment, the way Canvas's download URL serves them)
      const fid = path.split('/')[2];
      const f = Object.values(files).flat().find((x) => x.id === fid);
      const type = f?.['content-type'] || 'application/pdf';
      if (type.startsWith('image/')) { res.writeHead(200, { 'content-type': 'image/svg+xml', 'content-disposition': 'attachment' }); return res.end('<svg xmlns="http://www.w3.org/2000/svg" width="640" height="360"><rect width="640" height="360" fill="#f4f1e8"/><text x="24" y="60" font-size="28" font-family="sans-serif">Lecture 3 whiteboard</text></svg>'); }
      if (type.startsWith('text/')) { res.writeHead(200, { 'content-type': 'text/plain; charset=utf-8', 'content-disposition': 'attachment' }); return res.end('Reading list\n- Chapter 3\n- Chapter 4, sections 1-2\n'); }
      res.writeHead(200, { 'content-type': type, 'content-disposition': 'attachment' });
      return res.end('%PDF-1.4 mock');
    }
    // Canvas's own preview of a file, made to be framed. A course context only resolves the course's
    // own files: a submission attachment is the student's, so asking for it under /courses/:id is
    // the 404 page, exactly as real Canvas answers.
    if (/^\/(courses|groups)\/\w+\/files\/\w+\/file_preview$/.test(path)) {
      const fid = path.split('/')[4];
      if (!Object.values(files).flat().some((x) => x.id === fid)) {
        res.writeHead(404, { 'content-type': 'text/html; charset=utf-8' });
        return res.end('<html><body><h1>Whoops... Looks like nothing is here!</h1><p>Page Not Found</p></body></html>');
      }
      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
      return res.end('<html><body style="font-family:sans-serif;padding:20px"><div id="file_preview">Canvas file preview</div></body></html>');
    }
    if (/^\/files\/\w+\/file_preview$/.test(path)) { // the same preview with no context: any file the user can read
      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
      return res.end('<html><body style="font-family:sans-serif;padding:20px"><div id="file_preview" data-bcv-scope="no-context">Canvas file preview</div></body></html>');
    }
    if (path.startsWith('/equation_images/')) {
      // Canvas does not typeset a formula itself: it hands the LaTeX to its equation service and
      // serves back what that returns — an SVG sized in points, at the service's own text size, so
      // the browser renders it a third larger again and a stacked formula comes back two lines tall.
      let tex = path.slice('/equation_images/'.length);
      try { tex = decodeURIComponent(tex); } catch { /* leave it as it came */ }
      const tall = /\\frac|\\lim|\\sum|\\int|\\sqrt/.test(tex);
      const [w, hh] = tall ? [58, 34] : [30, 12];
      res.writeHead(200, { 'content-type': 'image/svg+xml' });
      return res.end(`<svg xmlns="http://www.w3.org/2000/svg" width="${w}pt" height="${hh}pt" viewBox="0 0 ${w} ${hh}"><text x="0" y="${hh - 2}" font-size="9" font-family="serif">${tex.replace(/[<>&]/g, '')}</text></svg>`);
    }
    if (path === '/logout') { // Canvas's logout: a DELETE (a POST with _method=delete) carrying the session's token
      const params = new URLSearchParams(raw);
      if (req.method === 'POST' && params.get('_method') === 'delete' && params.get('authenticity_token') === CSRF) {
        res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
        return res.end('<html><body><h1>Logged out</h1><p>You are logged out.</p></body></html>');
      }
      res.writeHead(req.method === 'GET' ? 200 : 422, { 'content-type': 'text/html; charset=utf-8' });
      return res.end(req.method === 'GET' ? '<html><body><h1>Log out?</h1><form method="post"><button>Log out</button></form></body></html>' : '<html><body><h1>Page Error</h1><p>There was a problem with your last request.</p></body></html>');
    }
    const handler = htmlPages[path];
    let qm;
    const slow = (fn) => setTimeout(fn, mockConfig.quizPageDelay || 0); // test-only: a slow quiz page, to watch the progress fill
    if (!handler && req.method === 'GET' && (qm = path.match(/^\/courses\/(\w+)\/quizzes\/(\w+)\/take(?:\/questions\/(\w+))?$/))) { // Canvas's own quiz-taking page
      const [cid, quizId, qid] = [qm[1], qm[2], qm[3] || url.searchParams.get('question_id')];
      return slow(() => {
        const html = takePage(cid, quizId, qid);
        if (!html) { res.writeHead(302, { location: `/courses/${cid}/quizzes/${quizId}` }); return res.end(); } // no open attempt: back to the quiz page, as Canvas does
        res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'set-cookie': `_csrf_token=${encodeURIComponent(CSRF)}; Path=/` });
        res.end(html);
      });
    }
    if (req.method === 'POST' && (qm = path.match(/^\/courses\/(\w+)\/quizzes\/(\w+)\/submissions\/([\w-]+)\/record_answer$/))) { // its Next / Previous
      const [cid, quizId, sid] = [qm[1], qm[2], qm[3]];
      return slow(() => { res.writeHead(302, { location: recordAnswer(cid, quizId, sid, raw) }); res.end(); });
    }
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'set-cookie': `_csrf_token=${encodeURIComponent(CSRF)}; Path=/` });
    res.end(handler ? handler() : page({ title: path.split('/').pop() || 'Canvas', courseId: (path.match(/^\/courses\/(\d+)/) || [])[1], body: `<h1>${path}</h1><p>Mock page rendered by Canvas.</p>` }));
  });
});

server.listen(port, () => console.log(`Mock Canvas listening on http://localhost:${port}`));
