#!/usr/bin/env node
// A tiny fake Canvas for local testing: a few HTML pages with Canvas-like
// markup and the handful of API endpoints the extension calls.
// Usage: node scripts/dev/mock-canvas.mjs [port]
import http from 'node:http';

const port = Number(process.argv[2] || process.env.PORT || 8787);
const now = Date.now();
const H = 3600e3;
const iso = (ms) => new Date(now + ms).toISOString();

const courses = [
  { id: '101', name: 'MATH 101: Calculus', shortName: 'MATH 101', code: 'MATH-101', color: '#BF32A4' },
  { id: '202', name: 'HIST 202: Modern Europe', shortName: 'HIST 202', code: 'HIST-202', color: '#0B874B' },
  { id: '303', name: 'CS 303: Algorithms', shortName: 'CS 303', code: 'CS-303', color: '#2D3B45' },
];

const planner = [
  item('assignment', '1', '101', 'Problem Set 3', 3 * H, 20),
  item('quiz', '2', '303', 'Quiz 2: Sorting', 20 * H, 10),
  item('discussion_topic', '3', '202', 'Week 5 discussion: Industrialization', 2 * 24 * H, 5),
  item('assignment', '4', '303', 'Project proposal', 6 * 24 * H, 50),
  item('assignment', '5', '101', 'Problem Set 2', -30 * H, 20, { missing: true }),
  item('assignment', '6', '202', 'Reading response 4', -2 * 24 * H, 10, { submitted: true, graded: true }),
  // Not graded: an ungraded discussion and a page with instructor to-do dates, and an event.
  scheduled('discussion_topic', '7', '303', 'Intro thread: say hello (ungraded)', 26 * H),
  scheduled('wiki_page', '8', '101', 'Read: Chapter 4 notes', 3 * 24 * H),
  scheduled('calendar_event', '9', '202', 'Guest lecture: Dr. Okafor', 4 * 24 * H),
];

function item(type, id, courseId, title, dueIn, points, sub = {}) {
  const course = courses.find((c) => c.id === courseId);
  const seg = type === 'quiz' ? 'quizzes' : type === 'discussion_topic' ? 'discussion_topics' : 'assignments';
  return {
    context_type: 'Course',
    course_id: courseId,
    context_name: course.shortName,
    plannable_id: id,
    plannable_type: type,
    plannable_date: iso(dueIn),
    plannable: { id, title, due_at: iso(dueIn), points_possible: points },
    planner_override: null,
    submissions: { submitted: !!sub.submitted, graded: !!sub.graded, missing: !!sub.missing, late: false, excused: false, needs_grading: false, has_feedback: !!sub.graded },
    html_url: `/courses/${courseId}/${seg}/${id}`,
  };
}

function scheduled(type, id, courseId, title, at) {
  const course = courses.find((c) => c.id === courseId);
  const seg = type === 'discussion_topic' ? 'discussion_topics' : type === 'wiki_page' ? 'pages' : 'calendar_events';
  const plannable = { id, title };
  if (type === 'calendar_event') plannable.start_at = iso(at);
  else plannable.todo_date = iso(at);
  return {
    context_type: 'Course',
    course_id: courseId,
    context_name: course.shortName,
    plannable_id: id,
    plannable_type: type,
    plannable_date: iso(at),
    plannable,
    planner_override: null,
    submissions: false,
    html_url: `/courses/${courseId}/${seg}/${id}`,
  };
}

const assignment = {
  id: '1', name: 'Problem Set 3', due_at: iso(3 * H), points_possible: 20, grading_type: 'points',
  submission_types: ['online_upload', 'online_text_entry'], allowed_attempts: 2,
  description: '<p>Complete problems <strong>3.1–3.8</strong> from the textbook. Show all work.</p><ul><li>Use the chain rule where appropriate.</li><li>Submit a single PDF.</li></ul>',
  rubric: [
    { id: 'c1', description: 'Correctness', long_description: 'Answers are correct.', points: 12, ratings: [{ id: 'r1', description: 'Full', points: 12 }, { id: 'r2', description: 'Partial', points: 6 }] },
    { id: 'c2', description: 'Work shown', long_description: 'Steps are legible and complete.', points: 8, ratings: [{ id: 'r3', description: 'Full', points: 8 }, { id: 'r4', description: 'Partial', points: 4 }] },
  ],
};
const submission = {
  workflow_state: 'graded', submitted_at: iso(-3 * 24 * H), score: 14, grade: '14', attempt: 1, late: false, missing: false, excused: false,
  rubric_assessment: { c1: { points: 8, rating_id: 'r2', comments: 'Sign error in 3.4 and 3.6.' }, c2: { points: 6, rating_id: 'r4', comments: '' } },
  submission_comments: [{ author_name: 'Dr. Rivera', created_at: iso(-2 * 24 * H), comment: 'Good effort. Watch your signs when differentiating composite functions, and label each step.' }],
};

function page({ title, path = '', courseId, body }) {
  const env = { current_user_id: '7', current_user: { display_name: 'Sam Student' }, COURSE_ID: courseId || null, context_asset_string: courseId ? `course_${courseId}` : 'user_7', TIMEZONE: 'America/New_York' };
  return `<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><title>${title}</title>
<meta name="csrf-token" content="mock-csrf">
<script>
  INST = {"environment":"development"};
  ENV = ${JSON.stringify(env)};
  BRANDS = {};
</script>
<style>
  :root{--ic-brand-primary:#0374B5;--ic-link-color:#0374B5;--ic-brand-global-nav-bgd:#394B58;--ic-brand-global-nav-ic-icon-svg-fill:#fff;--ic-brand-global-nav-menu-item__text-color:#fff}
  body{margin:0;font-family:-apple-system,BlinkMacSystemFont,sans-serif;color:#2d3b45;background:#fff}
  #header{position:fixed;left:0;top:0;bottom:0;width:84px;background:var(--ic-brand-global-nav-bgd);color:#fff}
  #menu{list-style:none;margin:0;padding:0}
  .ic-app-header__menu-list-item{text-align:center}
  .ic-app-header__menu-list-link{display:block;padding:12px 4px;color:#fff;text-decoration:none;font-size:11px}
  .menu-item-icon-container svg{width:26px;height:26px;fill:#fff}
  .ic-Layout-wrapper{margin-left:84px}
  .ic-app-nav-toggle-and-crumbs{padding:12px 24px;border-bottom:1px solid #c7cdd1}
  #main{display:flex}
  #not_right_side{flex:1;padding:0 24px}
  #right-side-wrapper{width:300px;padding:24px;border-left:1px solid #eee}
  .ic-Dashboard-header{display:flex;justify-content:space-between;align-items:center;padding:16px 0}
  .ic-DashboardCard__box__container{display:flex;flex-wrap:wrap;gap:24px}
  .ic-DashboardCard{width:262px;border-radius:4px;box-shadow:0 2px 4px rgba(0,0,0,.2);overflow:hidden}
  .ic-DashboardCard__header_hero{height:146px}
  .ic-DashboardCard__header_content{padding:12px}
  .ic-DashboardCard__header-title{font-weight:700;font-size:16px}
  .ic-DashboardCard__header-subtitle,.ic-DashboardCard__header-term{font-size:12px;color:#6b7780}
  .ic-DashboardCard__link{color:inherit;text-decoration:none;display:block}
  .ic-DashboardCard__action-container{display:flex;gap:16px;padding:8px 12px;border-top:1px solid #eee}
  .ig-list{list-style:none;padding:0;margin:0}
  .ig-row{display:flex;gap:12px;padding:14px 16px;border:1px solid #c7cdd1;border-top:0}
  .ig-info{flex:1}
  .ig-title{font-weight:700;color:var(--ic-link-color);text-decoration:none}
  .ig-details{font-size:12px;color:#6b7780}
  .ig-header{display:flex;justify-content:space-between;align-items:center;padding:12px 16px;background:#f5f5f5;border:1px solid #c7cdd1}
  .ig-header-title{margin:0;font-size:16px}
  #left-side{width:200px;flex:none;border-right:1px solid #c7cdd1;padding:12px 0}
  #section-tabs{list-style:none;margin:0;padding:0}
  #section-tabs a{display:block;padding:8px 16px;color:#2d3b45;text-decoration:none;border-left:3px solid transparent}
  #section-tabs a.active{border-left-color:#0374B5;font-weight:700;background:#fff}
  .header-bar{display:flex;gap:8px;align-items:center;padding:12px 0;border-bottom:1px solid #c7cdd1;margin-bottom:12px}
  .header-bar input{padding:6px 8px;border:1px solid #c7cdd1;flex:1}
  .btn{padding:6px 12px;border:1px solid #c7cdd1;background:#f5f5f5;border-radius:3px;font:inherit;cursor:pointer;text-decoration:none;color:#2d3b45}
  .ic-flash-success{padding:12px 16px;background:#00ac18;color:#fff;margin-bottom:12px}
  table.ic-Table{width:100%;border-collapse:collapse}
  table.ic-Table th,table.ic-Table td{border:1px solid #c7cdd1;padding:8px;text-align:left}
  .student-assignment-overview{list-style:none;padding:0;display:flex;gap:24px}
  .student-assignment-overview .title{font-weight:700;margin-right:6px}
  .user_content{max-width:760px;line-height:1.6}
  .btn-primary{background:var(--ic-brand-primary);color:#fff;padding:8px 14px;border-radius:4px;border:0}
  #footer{padding:40px 24px;color:#6b7780;font-size:12px}
  iframe{border:1px solid #ccc}
  .embed-box{width:640px}
</style></head>
<body class="with-right-side">
<div id="application" class="ic-app">
<header id="header" class="ic-app-header no-print">
  <ul id="menu" class="ic-app-header__menu-list">
    <li class="menu-item ic-app-header__menu-list-item"><a id="global_nav_dashboard_link" href="/" class="ic-app-header__menu-list-link"><div class="menu-item-icon-container"><svg viewBox="0 0 24 24"><rect x="3" y="3" width="8" height="8"/><rect x="13" y="3" width="8" height="8"/><rect x="3" y="13" width="8" height="8"/><rect x="13" y="13" width="8" height="8"/></svg></div><div class="menu-item__text">Dashboard</div></a></li>
    <li class="menu-item ic-app-header__menu-list-item"><a id="global_nav_courses_link" href="/courses" class="ic-app-header__menu-list-link"><div class="menu-item-icon-container"><svg viewBox="0 0 24 24"><rect x="4" y="3" width="16" height="18"/></svg></div><div class="menu-item__text">Courses</div></a></li>
    <li class="menu-item ic-app-header__menu-list-item"><a id="global_nav_calendar_link" href="/calendar" class="ic-app-header__menu-list-link"><div class="menu-item-icon-container"><svg viewBox="0 0 24 24"><rect x="3" y="5" width="18" height="16"/></svg></div><div class="menu-item__text">Calendar</div></a></li>
    <li class="menu-item ic-app-header__menu-list-item"><a href="/accounts/1/external_tools/9?launch_type=global_navigation" class="ic-app-header__menu-list-link"><div class="menu-item-icon-container"><svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/></svg></div><div class="menu-item__text">Studio</div></a></li>
    <li class="menu-item ic-app-header__menu-list-item"><a id="global_nav_help_link" href="#" class="ic-app-header__menu-list-link"><div class="menu-item-icon-container"><svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="9"/></svg></div><div class="menu-item__text">Help</div></a></li>
  </ul>
</header>
<div id="wrapper" class="ic-Layout-wrapper">
  <div class="ic-app-nav-toggle-and-crumbs"><nav id="breadcrumbs"><ul style="list-style:none;display:flex;gap:8px;margin:0;padding:0"><li><a href="/">Home</a></li>${courseId ? `<li><a href="/courses/${courseId}"><span>${courses.find((c) => c.id === courseId).name}</span></a></li>` : ''}<li>${title}</li></ul></nav></div>
  <div id="main" class="ic-Layout-columns">
    ${courseId ? `<div id="left-side" class="ic-app-course-menu ic-sticky-on list-view"><div id="sticky-container" class="ic-sticky-frame"><ul id="section-tabs">
      ${[['', 'Home'], ['/announcements', 'Announcements'], ['/assignments', 'Assignments'], ['/discussion_topics', 'Discussions'], ['/grades', 'Grades'], ['/modules', 'Modules'], ['/pages', 'Pages'], ['/files', 'Files'], ['/quizzes', 'Quizzes'], ['/users', 'People']]
        .map(([seg, label]) => `<li class="section"><a href="/courses/${courseId}${seg}" class="${label.toLowerCase()}${path === `/courses/${courseId}${seg}` ? ' active' : ''}"${path === `/courses/${courseId}${seg}` ? ' aria-current="page"' : ''}>${label}</a></li>`).join('')}
    </ul></div></div>` : ''}
    <div id="not_right_side" class="ic-app-main-content"><div id="content" class="ic-Layout-contentMain">${body}</div></div>
    <aside id="right-side-wrapper" class="ic-app-main-content__secondary"><div id="right-side"><h2>To Do</h2><p>Canvas's own sidebar</p></div></aside>
  </div>
  <footer id="footer" class="ic-app-footer">Canvas footer · Privacy · Terms</footer>
</div>
</div>
</body></html>`;
}

const pages = {
  '/': () => page({
    title: 'Dashboard',
    body: `<div id="dashboard_header_container"><div class="ic-Dashboard-header"><div class="ic-Dashboard-header__layout"><h1 class="ic-Dashboard-header__title"><span class="hidden-phone">Dashboard</span></h1><div class="ic-Dashboard-header__actions"><button class="Button">⋮</button></div></div></div></div><div id="dashboard">
      <div id="DashboardCard_Container"><div class="ic-DashboardCard__box"><div class="ic-DashboardCard__box__container">
      ${courses.map((c) => `<div class="ic-DashboardCard"><a class="ic-DashboardCard__link" href="/courses/${c.id}"><div class="ic-DashboardCard__header_hero" style="background:${c.color}"></div><div class="ic-DashboardCard__header_content"><h3 class="ic-DashboardCard__header-title">${c.shortName}</h3><div class="ic-DashboardCard__header-subtitle">${c.code}</div><div class="ic-DashboardCard__header-term">Fall 2026</div></div></a><div class="ic-DashboardCard__action-container"><span>📣</span><span>📝</span><span>💬</span></div></div>`).join('')}
      </div></div></div></div>`,
  }),
  '/courses/202': () => page({
    title: 'HIST 202: Modern Europe', courseId: '202', path: '/courses/202',
    body: `<div id="course_home_content"><h1>HIST 202: Modern Europe</h1><div class="user_content"><p>Welcome to Modern Europe. This week: <strong>industrialization</strong> and its effects on family life.</p><p><img src="data:image/svg+xml;utf8,${encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="480" height="180"><rect width="480" height="180" fill="#dbe4ee"/><text x="20" y="100" font-family="sans-serif" font-size="24" fill="#2d3b45">Course banner image</text></svg>')}" width="480" height="180" alt=""></p></div>
      <div class="header-bar"><input type="text" placeholder="Search course…" aria-label="Search"><button class="btn">Filter</button><button class="btn btn-primary">New</button></div></div>`,
  }),
  '/courses/303/modules': () => page({
    title: 'Modules', courseId: '303', path: '/courses/303/modules',
    body: `<h1>Modules</h1><div id="context_modules" class="ig-list">
      <div class="item-group-condensed context_module" id="context_module_1"><div class="ig-header header"><h2 class="ig-header-title"><span class="name">Week 1: Complexity</span></h2><button class="btn btn-small">Collapse</button></div><div class="content"><ul class="ig-list items context_module_items">
        <li class="context_module_item indent_0"><div class="ig-row ig-published"><div class="ig-info"><a class="ig-title" href="/courses/303/pages/big-o-notes">Big-O notes</a><div class="ig-details"><span class="ig-details__item">Page</span></div></div></div></li>
        <li class="context_module_item indent_1"><div class="ig-row ig-published"><div class="ig-info"><a class="ig-title" href="/courses/303/quizzes/2">Quiz 2: Sorting</a><div class="ig-details"><span class="ig-details__item">10 pts</span></div></div></div></li>
        <li class="context_module_item indent_0"><div class="ig-row ig-published"><div class="ig-info"><a class="ig-title" href="/courses/303/assignments/4">Project proposal</a><div class="ig-details"><span class="ig-details__item">50 pts</span></div></div></div></li>
        <li class="context_module_item indent_0"><div class="ig-row ig-published"><div class="ig-info"><a class="ig-title" href="/courses/303/discussion_topics/7">Intro thread: say hello (ungraded)</a><div class="ig-details"><span class="ig-details__item">Discussion</span></div></div></div></li>
      </ul></div></div>
      <div class="item-group-condensed context_module" id="context_module_2"><div class="ig-header header"><h2 class="ig-header-title"><span class="name">Week 2: Sorting</span></h2></div><div class="content"><ul class="ig-list items context_module_items">
        <li class="context_module_item indent_0"><div class="ig-row ig-published"><div class="ig-info"><a class="ig-title" href="/courses/303/pages/merge-sort">Merge sort</a><div class="ig-details"><span class="ig-details__item">Page</span></div></div></div></li>
      </ul></div></div></div>`,
  }),
  '/courses/101/grades': () => page({
    title: 'Grades', courseId: '101', path: '/courses/101/grades',
    body: `<h1>Grades for Sam Student</h1><div class="ic-flash-success">Grades were updated.</div><table id="grades_summary" class="ic-Table ic-Table--hover-row"><thead><tr><th>Name</th><th>Due</th><th>Status</th><th>Score</th><th>Out of</th></tr></thead><tbody>
      <tr class="student_assignment assignment_graded"><th class="title"><a href="/courses/101/assignments/6">Reading response 4</a><div class="context">Homework</div></th><td class="due">Sep 8</td><td class="status">—</td><td class="assignment_score"><span class="grade">9</span></td><td>10</td></tr>
      <tr class="student_assignment"><th class="title"><a href="/courses/101/assignments/5">Problem Set 2</a><div class="context">Homework</div></th><td class="due">Sep 9</td><td class="status">missing</td><td class="assignment_score"><span class="grade">–</span></td><td>20</td></tr>
      <tr class="student_assignment"><th class="title"><a href="/courses/101/assignments/1">Problem Set 3</a><div class="context">Homework</div></th><td class="due">Sep 10</td><td class="status"></td><td class="assignment_score"><span class="grade">–</span></td><td>20</td></tr>
      <tr class="group_total"><th class="title">Homework</th><td></td><td></td><td class="assignment_score">90%</td><td></td></tr>
      <tr class="final_grade"><th class="title">Total</th><td></td><td></td><td class="assignment_score">87.5%</td><td></td></tr>
      </tbody></table>`,
  }),
  '/courses/101/assignments': () => page({
    title: 'Assignments', courseId: '101', path: '/courses/101/assignments',
    body: `<h1>Assignments</h1><div class="header-bar"><input type="search" placeholder="Search for assignment" aria-label="Search"><button class="btn">Show by type</button></div><ul class="ig-list">
      <li class="assignment"><div class="ig-row"><div class="ig-info"><a class="ig-title" href="/courses/101/assignments/1">Problem Set 3</a><div class="ig-details"><span class="ig-details__item">Due Sep 10 at 11:59pm</span> · 20 pts</div></div></div></li>
      <li class="assignment"><div class="ig-row"><div class="ig-info"><a class="ig-title" href="/courses/101/assignments/5">Problem Set 2</a><div class="ig-details">20 pts</div></div></div></li>
      <li class="assignment"><div class="ig-row"><div class="ig-info"><a class="ig-title" href="/courses/101/assignments/9">Problem Set 4 (no date yet)</a><div class="ig-details">20 pts</div></div></div></li>
    </ul>`,
  }),
  '/courses/101/assignments/1': () => page({
    title: 'Problem Set 3', courseId: '101', path: '/courses/101/assignments/1',
    body: `<div id="assignment_show"><h1 class="title">Problem Set 3</h1><ul class="student-assignment-overview"><li><span class="title">Due</span><span class="value">${new Date(now + 3 * H).toLocaleString()}</span></li><li><span class="title">Points</span><span class="value">20</span></li><li><span class="title">Submitting</span><span class="value">a file upload</span></li></ul>
      <div class="description user_content">${assignment.description}</div>
      <div class="embed-box"><iframe id="tool_content" name="tool_content" src="/courses/101/external_tools/retrieve?url=https%3A%2F%2Ftool.example.com%2Flaunch" width="640" height="320" title="Embedded tool"></iframe></div>
      <p><iframe src="about:blank" name="lti_launch_frame" width="640" height="200" title="LTI 1.3"></iframe><form action="/courses/101/external_tools/55/launch" method="post" target="lti_launch_frame"><input type="hidden" name="id_token" value="x"></form></p>
      <button class="btn-primary">Start Assignment</button></div>`,
  }),
  '/courses/202/discussion_topics/3': () => page({
    title: 'Week 5 discussion: Industrialization', courseId: '202', path: '/courses/202/discussion_topics/3',
    body: `<h1>Week 5 discussion: Industrialization</h1><div class="user_content"><p>How did industrialization change family life in 19th-century Europe? Respond in 200 words and reply to two classmates.</p></div>
      <div><h2>Reply</h2><textarea id="discussion_reply" rows="4" style="width:600px"></textarea></div>`,
  }),
};

const apiRoutes = [
  [/^\/api\/v1\/planner\/items/, () => planner],
  [/^\/api\/v1\/dashboard\/dashboard_cards/, () => courses.map((c) => ({ id: c.id, shortName: c.shortName, originalName: c.name, courseCode: c.code, href: `/courses/${c.id}`, term: 'Fall 2026' }))],
  [/^\/api\/v1\/users\/self\/colors/, () => ({ custom_colors: Object.fromEntries(courses.map((c) => [`course_${c.id}`, c.color])) })],
  [/^\/api\/v1\/courses\/101\/assignments\/1\/submissions\/self/, () => submission],
  [/^\/api\/v1\/courses\/101\/assignments\/1/, () => assignment],
  [/^\/api\/v1\/courses\/202\/discussion_topics\/3\/view/, () => ({ participants: [{ id: '8', display_name: 'Priya' }], view: [{ id: '1', user_id: '8', created_at: iso(-5 * H), message: '<p>Factory work pulled children out of the home, which changed who raised them.</p>', replies: [] }] })],
  [/^\/api\/v1\/courses\/202\/discussion_topics\/3/, () => ({ id: '3', title: 'Week 5 discussion: Industrialization', message: '<p>How did industrialization change family life in 19th-century Europe? Respond in 200 words and reply to two classmates.</p>', posted_at: iso(-3 * 24 * H), author: { display_name: 'Prof. Adler' }, assignment: { due_at: iso(2 * 24 * H), points_possible: 5 } })],
  [/^\/api\/v1\/courses\/\d+\/enrollments/, () => [{ grades: { current_score: 87.5, current_grade: 'B+', final_score: 80 } }]],
];

const server = http.createServer((req, res) => {
  const url = new URL(req.url, `http://localhost:${port}`);
  const path = url.pathname.replace(/\/+$/, '') || '/';
  if (path.startsWith('/api/')) {
    if (req.method === 'POST' && path === '/api/v1/planner/overrides') {
      let body = '';
      req.on('data', (c) => (body += c));
      req.on('end', () => {
        const b = JSON.parse(body || '{}');
        const it = planner.find((p) => p.plannable_type === b.plannable_type && String(p.plannable_id) === String(b.plannable_id));
        if (it) it.planner_override = { id: 'ov' + it.plannable_id, marked_complete: !!b.marked_complete };
        res.writeHead(200, { 'content-type': 'application/json' });
        res.end('while(1);' + JSON.stringify(it?.planner_override || {}));
      });
      return;
    }
    for (const [re, handler] of apiRoutes) {
      if (re.test(path)) {
        res.writeHead(200, { 'content-type': 'application/json; charset=utf-8' });
        res.end('while(1);' + JSON.stringify(handler(url)));
        return;
      }
    }
    res.writeHead(404, { 'content-type': 'application/json' });
    res.end('while(1);' + JSON.stringify({ errors: [{ message: 'not found' }] }));
    return;
  }
  if (path.startsWith('/courses/101/external_tools')) {
    res.writeHead(200, { 'content-type': 'text/html' });
    res.end('<html><body style="font-family:sans-serif;padding:20px">Embedded tool content</body></html>');
    return;
  }
  const handler = pages[path];
  if (!handler) {
    res.writeHead(200, { 'content-type': 'text/html' });
    res.end(page({ title: path, body: `<h1>${path}</h1><p>Mock page.</p>` }));
    return;
  }
  res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
  res.end(handler());
});

server.listen(port, () => console.log(`Mock Canvas listening on http://localhost:${port}`));
