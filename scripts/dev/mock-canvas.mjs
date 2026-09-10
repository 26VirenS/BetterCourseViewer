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
const courseById = (id) => courses.find((c) => c.id === String(id));
const fullCourse = (c) => ({
  id: c.id, name: c.name, course_code: c.code, original_name: undefined, term: c.term || term, is_favorite: favorites.has(c.id), default_view: c.default_view || 'wiki',
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
    ['1006', 'Functions and Their Representations', 'Effort', 30, 30, -6, 23.98, -12, {}],
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
    ['2003', 'Lab 2', 'Labs', 20, null, 2, 23.98, null, {}],
    ['2004', 'W3 HW', 'Homework', 15, null, 6, 23.98, null, {}],
  ],
  103: [
    ['3001', 'Prelab 2', 'Prelabs', 15, 15, -4, 23.98, -5, {}],
    ['3002', 'Prelab 3', 'Prelabs', 15, null, 3, 23.98, null, {}],
  ],
  104: [
    ['4001', 'Week 1 reflection', 'Assignments', 10, 10, -6, 23.98, -7, {}],
    // the mockup's example: file or text or link, four file types, open for a week, unlimited attempts
    ['4002', 'Week 2 Post Class Assignment: GC articles', 'Assignments', 10, null, 0, 23.98, null, { types: ['online_upload', 'online_text_entry', 'online_url'], ext: ['pdf', 'docx', 'png', 'jpg'], window: true, attempts: -1, description: '<p>Submit the Grand Challenge you chose with an explanation of why it matters, plus three scientific papers or news articles in APA or MLA format.</p>' }],
    ['4003', 'Knewton Alta: Unit 2', 'Assignments', 20, null, 5, 23.98, null, { tool: 'https://tool.example.com/launch' }],
  ],
  105: [
    ['5001', 'Journal #1', 'Journals', 5, 5, -6, 23.98, -6, {}],
    ['5002', 'Research Day Activity: Choosing a Field Site', 'Activities', 5, null, 0, 23.98, null, {}],
    ['5003', 'Journal #2', 'Journals', 5, null, 1, 23.98, null, {}],
  ],
  106: [], 201: [], 202: [], 301: [['9001', 'Final project', 'Projects', 100, 97, -110, 23.98, -111, {}]],
};
const GROUPS = { 101: [['Discussion Quizzes', 18], ['Midterms', 57], ['Final', 25], ['Effort', 0], ['Collaboration', 0], ['Coursework (Knewton Alta)', 0]] };
const rubric = [
  { id: 'c1', description: 'Correctness', long_description: 'Answers are correct.', points: 6, ratings: [{ id: 'r1', description: 'Full', points: 6 }, { id: 'r2', description: 'Partial', points: 3 }] },
  { id: 'c2', description: 'Work shown', long_description: 'Steps are legible and complete.', points: 4, ratings: [{ id: 'r3', description: 'Full', points: 4 }, { id: 'r4', description: 'Partial', points: 2 }] },
];
function assignmentObj(courseId, row) {
  const [id, name, group, possible, earned, dueDay, dueHour, subDay, extra] = row;
  const c = courseById(courseId);
  const groups = (GROUPS[courseId] || [[Object.keys(groupNames(courseId))[0], 0]]);
  const gIdx = groups.findIndex(([g]) => g === group);
  const due = at(dueDay, Math.floor(dueHour), Math.round((dueHour % 1) * 60));
  const submitted = subDay !== null || earned !== null;
  const submission = {
    id: `s${id}`, assignment_id: id, workflow_state: earned !== null ? 'graded' : submitted ? 'submitted' : 'unsubmitted', score: earned, grade: earned === null ? null : String(earned),
    submitted_at: subDay !== null ? at(subDay, 15, 52) : (earned !== null ? at(dueDay - 1, 16, 1) : null), graded_at: earned !== null ? at(dueDay, 8, 0) : null,
    late: !!extra.late, missing: false, excused: false, attempt: submitted ? 1 : null,
    submission_comments: extra.rubric ? [] : (earned !== null && id === '1002' ? [{ author_name: c.teacher, created_at: at(dueDay + 1, 9, 0), comment: 'Check the domain restrictions in question 3 — the rest was solid.' }] : []),
    rubric_assessment: extra.rubric && earned !== null ? { c1: { points: 4, rating_id: 'r2', comments: 'Sign error in part b.' }, c2: { points: 4, rating_id: 'r3' } } : undefined,
  };
  return {
    id, name, description: extra.description || `<p>Complete <strong>${name}</strong> as described in lecture. Show all work and submit a single PDF.</p><ul><li>Use the chain rule where appropriate.</li><li>Label each step.</li></ul>${extra.rubric ? '<p>See the rubric for how points are awarded.</p>' : ''}`,
    due_at: due, lock_at: extra.window ? at(dueDay, 23, 59) : null, unlock_at: extra.window ? at(dueDay - 7, 0, 0) : null, points_possible: possible, grading_type: 'points', published: true, html_url: `/courses/${courseId}/assignments/${id}`,
    submission_types: extra.quiz ? ['online_quiz'] : extra.tool ? ['external_tool'] : extra.types || ['online_upload', 'online_text_entry'], is_quiz_assignment: !!extra.quiz, quiz_id: extra.quiz ? String(Number(id) + 8000) : undefined,
    allowed_extensions: extra.ext || [], locked_for_user: false,
    external_tool_tag_attributes: extra.tool ? { url: extra.tool, new_tab: false, resource_link_id: 'rl1' } : undefined,
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
function plannerItems() {
  const items = [];
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
  return t ? { ...t, html_url: `/courses/${courseId}/discussion_topics/${t.id}`, locked: false, require_initial_post: false, attachments: [] } : null;
};
const entries = new Map();
const viewFor = (topicId) => ({
  participants: [{ id: '8', display_name: 'Alan Aguilar', avatar_image_url: null }, { id: '9', display_name: 'Wail Ahmed', avatar_image_url: null }, { id: '7', display_name: 'Sam Student', avatar_image_url: null }],
  view: [{ id: 'e1', user_id: '8', created_at: ago(20 * H), message: '<p>Factory work pulled children out of the home, which changed who raised them.</p>', replies: [{ id: 'e2', user_id: '9', created_at: ago(10 * H), message: '<p>Agreed, and schooling laws followed.</p>', replies: [] }] }, ...(entries.get(topicId) || [])],
});
const pages = {
  101: [
    { url: 'course-information', title: 'Course Information', front_page: true, created_at: ago(18 * D), updated_at: ago(15 * D), last_edited_by: { display_name: 'Yue Lei' }, body: '<p><strong>Ask any question:</strong> use the <a href="/courses/101/discussion_topics">Discussions</a> page — please do not use “Ask your instructor a question.”</p><p><strong>Please read</strong> the <a href="/courses/101/files/f1">Course Syllabus</a> in Files → Course Information for all course policies, structure, materials and exam dates. A tentative schedule of lecture topics is in <a href="/courses/101/files/f2">Lecture schedule</a>.</p><p>Slides and worksheets used in lectures and discussion sections are under <a href="/courses/101/files">Files</a>. The textbook is a free online book on OpenStax, <em>Calculus Volume 1</em>. See <a href="https://example.edu/store">Knewton Alta</a> for access.</p>' },
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
  ],
  f101a: [{ id: 'f3', display_name: 'Resources_Policy.pdf', filename: 'Resources_Policy.pdf', 'content-type': 'application/pdf', size: 130000, updated_at: ago(16 * D), url: '/files/f3/download' }],
  f101b: [{ id: 'f4', display_name: 'Dis01 worksheet.pdf', filename: 'dis01.pdf', 'content-type': 'application/pdf', size: 80000, updated_at: ago(3 * D), url: '/files/f4/download' }],
  f101c: [],
};
const quizzes = (courseId) => allAssignments(courseId).filter((a) => a.is_quiz_assignment).map((a, i) => ({ id: a.quiz_id, title: a.name, due_at: a.due_at, points_possible: a.points_possible, question_count: 4, quiz_type: a.name === 'Skills_Check' ? 'practice_quiz' : 'assignment', time_limit: 20, allowed_attempts: 1, description: `<p>${a.name}: four questions on the pre-lecture reading.</p>`, html_url: `/courses/${courseId}/quizzes/${a.quiz_id}`, locked_for_user: false, assignment_id: a.id, one_question_at_a_time: a.name === 'Lec07-PreQuiz', cant_go_back: a.name === 'Lec07-PreQuiz', hide_results: null, shuffle_answers: false }));

// ---- quiz attempts (stateful, like Canvas's quiz submission API) --------------------------
const quizSubs = new Map([['9001', [{ id: 'qs1', quiz_id: '9001', attempt: 1, score: 13, kept_score: 13, started_at: at(-14, 15, 30), finished_at: at(-14, 15, 52), workflow_state: 'complete', validation_token: 'tok-1', state: {} }]]]);
const quizQuestionBank = (quizId) => {
  const mc = (n, text, opts) => ({ id: `${quizId}${n}`, position: n, question_name: `Question ${n}`, question_type: 'multiple_choice_question', question_text: `<p>${text}</p>`, points_possible: 4, answers: opts.map((t, i) => ({ id: Number(`${quizId}${n}${i + 1}`), text: t, html: '', weight: i === 0 ? 100 : 0 })) });
  return [
    mc(1, 'What is the velocity at t = 5?', ['-3.15 m/s', '-2 m/s', '0 m/s', '1.37 m/s', 'None of the above']),
    mc(2, 'What is the displacement between t = 0 and t = 5?', ['-3.15 m', '-2 m', '0 m', '17.68 m', 'None of the above']),
    { id: `${quizId}3`, position: 3, question_name: 'Question 3', question_type: 'multiple_answers_question', question_text: '<p>Which of these are vector quantities?</p>', points_possible: 4, answers: [{ id: Number(`${quizId}31`), text: 'Velocity', weight: 100 }, { id: Number(`${quizId}32`), text: 'Speed', weight: 0 }, { id: Number(`${quizId}33`), text: 'Acceleration', weight: 100 }] },
    { id: `${quizId}4`, position: 4, question_name: 'Question 4', question_type: 'numerical_question', question_text: '<p>At what time (in seconds) is the object momentarily at rest? See the <a href="/courses/101/pages/chapter-4-notes">chapter 4 notes</a>.</p>', points_possible: 5, answers: [{ id: Number(`${quizId}41`), text: '3.15', weight: 100, exact: 3.15 }] },
  ];
};
const pubSub = ({ state, ...s }) => s;
const findSub = (id) => [...quizSubs.values()].flat().find((s) => s.id === id) || null;
const subQuestions = (s) => ({
  quiz_submission_questions: quizQuestionBank(s.quiz_id).map((q) => ({ id: q.id, position: q.position, flagged: !!s.state[q.id]?.flagged, answer: s.state[q.id]?.answer ?? null })),
  quiz_questions: quizQuestionBank(s.quiz_id),
});
const modules = {
  102: [
    { id: 'm1', name: 'Week 1: Kinematics', state: 'completed', items: [{ id: 'i1', type: 'Page', title: 'Big picture', html_url: '/courses/102/pages/big-picture', completion_requirement: { type: 'must_view', completed: true } }, { id: 'i2', type: 'Assignment', title: 'Lab 1 report', html_url: '/courses/102/assignments/2001', content_details: { due_at: at(-7, 23, 59), points_possible: 20 }, completion_requirement: { type: 'must_submit', completed: true } }] },
    { id: 'm2', name: 'Week 2: Forces', state: 'started', items: [{ id: 'i3', type: 'SubHeader', title: 'Before class' }, { id: 'i4', type: 'Page', title: 'Newton’s laws', html_url: '/courses/102/pages/newtons-laws', indent: 1, completion_requirement: { type: 'must_view', completed: true } }, { id: 'i5', type: 'Assignment', title: 'W2 HW', html_url: '/courses/102/assignments/2002', indent: 1, content_details: { due_at: at(-1, 23, 59), points_possible: 15 }, completion_requirement: { type: 'must_submit', completed: false } }, { id: 'i6', type: 'ExternalUrl', title: 'PhET simulation', external_url: 'https://phet.colorado.edu', html_url: 'https://phet.colorado.edu' }] },
    { id: 'm3', name: 'Week 3: Energy', state: 'locked', unlock_at: at(5, 8, 0), items: [] },
  ],
  101: [{ id: 'm11', name: 'Unit 1: Functions', state: 'started', items: [{ id: 'i11', type: 'Page', title: 'Course Information', html_url: '/courses/101/pages/course-information' }, { id: 'i12', type: 'Quiz', title: 'Lec06-PreQuiz', html_url: '/courses/101/quizzes/9011', content_details: { due_at: at(1, 10, 30), points_possible: 17 } }] }],
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
<meta name="csrf-token" content="mock-csrf">
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
<header id="header" class="ic-app-header no-print"><div class="ic-app-header__logomark-container"><a href="/" class="ic-app-header__logomark"></a></div><ul id="menu"><li><a id="global_nav_dashboard_link" href="/" class="ic-app-header__menu-list-link">Dashboard</a></li><li><a id="global_nav_courses_link" href="/courses" class="ic-app-header__menu-list-link">Courses</a></li></ul></header>
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
  // a homework-submission tool's own picker, framed by the assignment page exactly as Canvas frames it;
  // when a file is chosen the return page posts externalContentReady to the window that framed it
  '/courses/104/external_tools/t1/resource_selection': () => `<!DOCTYPE html><html><head><meta charset="utf-8"><title>Box</title></head><body style="font-family:sans-serif;padding:24px"><h2 id="tool-title">Box picker (the tool's own page)</h2><p>The tool owns everything here. Choosing a file hands it back to the assignment page.</p><button id="pick" onclick="window.parent.postMessage({ subject: 'externalContentReady', service: 'external_tool_dialog', contents: [{ '@type': 'FileItem', url: 'http://localhost:${port}/files/box1/download', text: 'GC-articles-Sharma.pdf', mediaType: 'application/pdf' }] }, '*')">Use GC-articles-Sharma.pdf</button></body></html>`,
  '/courses/101/quizzes/9011/take': () => page({ title: 'Lec06-PreQuiz', courseId: '101', body: '<h1>Lec06-PreQuiz</h1><form id="submit_quiz_form"><p>Question 1 of 4</p><label><input type="radio" name="q1"> A</label> <label><input type="radio" name="q1"> B</label><p><button type="button" class="btn">Submit Quiz</button></p></form>' }),
};

// ---- API routing ------------------------------------------------------------------------------
const routes = [];
const on = (method, re, handler) => routes.push([method, re, handler]);
const json = (res, data, status = 200) => {
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8' });
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
on('GET', /^\/api\/v1\/dashboard\/dashboard_cards$/, () => courses.filter((c) => favorites.has(c.id)).map((c) => ({ id: c.id, shortName: c.name, originalName: c.name, courseCode: c.code, href: `/courses/${c.id}`, term: 'Fall 2026', subtitle: c.section, links: [{ css_class: 'announcements', label: 'Announcements', path: `/courses/${c.id}/announcements` }, { css_class: 'assignments', label: 'Assignments', path: `/courses/${c.id}/assignments` }, { css_class: 'discussions', label: 'Discussions', path: `/courses/${c.id}/discussion_topics` }, { css_class: 'files', label: 'Files', path: `/courses/${c.id}/files` }] })));
on('GET', /^\/api\/v1\/users\/self\/colors$/, () => ({ custom_colors: Object.fromEntries(courses.map((c) => [`course_${c.id}`, c.color])) }));
on('POST', /^\/api\/v1\/users\/self\/favorites\/courses\/(\w+)$/, (url, m) => { favorites.add(m[1]); return { context_id: m[1], context_type: 'Course' }; });
on('DELETE', /^\/api\/v1\/users\/self\/favorites\/courses\/(\w+)$/, (url, m) => { favorites.delete(m[1]); return { context_id: m[1] }; });
on('GET', /^\/api\/v1\/planner\/items$/, (url) => filterDates(plannerItems(), url, 'plannable_date'));
on('POST', /^\/api\/v1\/planner\/overrides$/, (url, m, body) => { const ov = { id: `ov${overrides.size + 1}`, plannable_type: body.plannable_type, plannable_id: body.plannable_id, marked_complete: !!body.marked_complete, dismissed: !!body.dismissed }; overrides.set(`${body.plannable_type}:${body.plannable_id}`, ov); return ov; });
on('PUT', /^\/api\/v1\/planner\/overrides\/(\w+)$/, (url, m, body) => { for (const ov of overrides.values()) if (ov.id === m[1]) { Object.assign(ov, body); return ov; } return {}; });
on('GET', /^\/api\/v1\/users\/self\/activity_stream\/summary$/, () => [{ type: 'Announcement', unread_count: 3, count: 5 }, { type: 'Conversation', unread_count: 1, count: 2 }, { type: 'DiscussionTopic', unread_count: 4, count: 6 }]);
on('GET', /^\/api\/v1\/users\/self\/activity_stream$/, () => [
  { id: 'a1', type: 'Announcement', announcement_id: '8001', title: 'Prerequisite Skills Test', message: '<p>Good morning everyone, the results from the Skills_Check test have been posted…</p>', course_id: '101', context_type: 'Course', read_state: false, updated_at: ago(6 * D), html_url: '/courses/101/announcements/8001' },
  { id: 'a2', type: 'DiscussionTopic', discussion_topic_id: '7003', title: 'Is there any discussion happening this week?', message: '<p>Last post by Alan Aguilar.</p>', course_id: '101', context_type: 'Course', read_state: false, updated_at: ago(18 * H), total_root_discussion_entries: 23, html_url: '/courses/101/discussion_topics/7003' },
  { id: 'a3', type: 'Submission', title: 'Lec05-PreQuiz graded — 19 / 19', message: '<p>Effort group.</p>', course_id: '101', context_type: 'Course', read_state: true, updated_at: ago(D), html_url: '/courses/101/assignments/1007' },
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
on('GET', /^\/api\/v1\/users\/self\/groups$/, () => groupList);
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
// test-only switches: POST /__mock/config {"calendarFail": true}
const mockConfig = { calendarFail: false };
on('POST', /^\/__mock\/config$/, (url, m, body) => Object.assign(mockConfig, body));
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
on('GET', /^\/api\/v1\/courses\/(\w+)\/assignments\/(\w+)\/submissions\/self$/, (url, m) => (allAssignments(m[1]).find((a) => a.id === m[2]) || {}).submission || null);
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
on('GET', /^\/api\/v1\/courses\/(\w+)\/quizzes\/(\w+)\/submissions$/, (url, m) => ({ quiz_submissions: (quizSubs.get(m[2]) || []).map(pubSub) }));
on('POST', /^\/api\/v1\/courses\/(\w+)\/quizzes\/(\w+)\/submissions$/, (url, m) => {
  const list = quizSubs.get(m[2]) || [];
  const q = quizzes(m[1]).find((x) => x.id === m[2]);
  if (!q) return null;
  const s = { id: `qs${m[2]}-${list.length + 1}`, quiz_id: m[2], user_id: '7', attempt: list.length + 1, started_at: new Date().toISOString(), end_at: q.time_limit ? new Date(Date.now() + q.time_limit * 60e3).toISOString() : null, finished_at: null, workflow_state: 'untaken', validation_token: `tok-${m[2]}-${list.length + 1}`, score: null, kept_score: null, state: {} };
  list.push(s);
  quizSubs.set(m[2], list);
  return { quiz_submissions: [pubSub(s)] };
});
on('GET', /^\/api\/v1\/quiz_submissions\/([\w-]+)\/questions$/, (url, m) => { const s = findSub(m[1]); return s ? subQuestions(s) : null; });
on('POST', /^\/api\/v1\/quiz_submissions\/([\w-]+)\/questions$/, (url, m, body) => {
  const s = findSub(m[1]);
  if (!s || body.validation_token !== s.validation_token) return null;
  for (const q of body.quiz_questions || []) s.state[String(q.id)] = { ...(s.state[String(q.id)] || {}), answer: q.answer };
  return subQuestions(s);
});
on('PUT', /^\/api\/v1\/quiz_submissions\/([\w-]+)\/questions\/(\w+)\/(flag|unflag)$/, (url, m) => { const s = findSub(m[1]); if (!s) return null; s.state[m[2]] = { ...(s.state[m[2]] || {}), flagged: m[3] === 'flag' }; return subQuestions(s); });
on('GET', /^\/api\/v1\/courses\/(\w+)\/quizzes\/(\w+)\/submissions\/([\w-]+)\/time$/, (url, m) => { const s = findSub(m[3]); return s ? { end_at: s.end_at, time_left: s.end_at ? Math.round((new Date(s.end_at) - Date.now()) / 1000) : null } : null; });
on('POST', /^\/api\/v1\/courses\/(\w+)\/quizzes\/(\w+)\/submissions\/([\w-]+)\/complete$/, (url, m) => {
  const s = findSub(m[3]);
  if (!s) return null;
  s.workflow_state = 'complete';
  s.finished_at = new Date().toISOString();
  s.score = quizQuestionBank(s.quiz_id).reduce((sum, q) => {
    const a = s.state[q.id]?.answer;
    const right = q.answers.filter((x) => x.weight === 100).map((x) => String(x.id));
    if (a === null || a === undefined || a === '') return sum;
    if (q.question_type === 'numerical_question') return sum + (Number(a) === q.answers[0].exact ? q.points_possible : 0);
    const picked = (Array.isArray(a) ? a : [a]).map(String).sort();
    return sum + (picked.join() === right.sort().join() ? q.points_possible : 0);
  }, 0);
  s.kept_score = s.score;
  return { quiz_submissions: [pubSub(s)] };
});
on('GET', /^\/api\/v1\/courses\/(\w+)\/quizzes\/(\w+)$/, (url, m) => quizzes(m[1]).find((q) => q.id === m[2]) || null);
on('GET', /^\/api\/v1\/courses\/(\w+)\/quizzes$/, (url, m) => quizzes(m[1]));
on('GET', /^\/api\/v1\/courses\/(\w+)\/modules$/, (url, m) => modules[m[1]] || []);

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
    // like Canvas: every write needs the page's CSRF token, body or not (file storage is a separate service and has none)
    if (req.method !== 'GET' && !path.startsWith('/__mock/') && !path.startsWith('/__upload/') && req.headers['x-csrf-token'] !== 'mock-csrf') return json(res, { errors: [{ message: 'invalid authenticity token' }] }, 422);
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
    if (path.startsWith('/files/')) {
      res.writeHead(200, { 'content-type': 'application/pdf' });
      return res.end('%PDF-1.4 mock');
    }
    const handler = htmlPages[path];
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
    res.end(handler ? handler() : page({ title: path.split('/').pop() || 'Canvas', courseId: (path.match(/^\/courses\/(\d+)/) || [])[1], body: `<h1>${path}</h1><p>Mock page rendered by Canvas.</p>` }));
  });
});

server.listen(port, () => console.log(`Mock Canvas listening on http://localhost:${port}`));
