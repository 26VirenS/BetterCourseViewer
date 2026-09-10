/* Figures out where we are in Canvas (route, ids, ENV) and extracts the
 * content of the current page for the smart assistant. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { htmlToText, elementText } = BCV.utils;

  const MAX_CONTEXT_CHARS = 60000;

  /** Extract the inline `ENV = {...}` object Canvas embeds in every page. */
  function readEnv() {
    for (const s of document.scripts) {
      if (s.src) continue;
      const t = s.textContent;
      if (!t || t.length > 3e6) continue;
      const i = t.indexOf('ENV = {');
      if (i < 0) continue;
      const json = balanced(t, i + 'ENV = '.length);
      if (!json) continue;
      try {
        return JSON.parse(json);
      } catch {
        /* keep looking */
      }
    }
    return {};
  }

  function balanced(text, start) {
    let depth = 0;
    let inStr = false;
    let esc = false;
    for (let i = start; i < text.length; i++) {
      const c = text[i];
      if (inStr) {
        if (esc) esc = false;
        else if (c === '\\') esc = true;
        else if (c === '"') inStr = false;
        continue;
      }
      if (c === '"') inStr = true;
      else if (c === '{') depth++;
      else if (c === '}') {
        depth--;
        if (depth === 0) return text.slice(start, i + 1);
      }
    }
    return null;
  }

  function isCanvasPage() {
    return !!(document.getElementById('application') || document.querySelector('.ic-app-header, #global_nav_dashboard_link, meta[name="csrf-token"]'));
  }

  const ROUTES = [
    [/^\/$/, 'dashboard'],
    [/^\/dashboard/, 'dashboard'],
    [/^\/courses\/?$/, 'courses'],
    [/^\/calendar/, 'calendar'],
    [/^\/conversations/, 'inbox'],
    [/^\/profile/, 'profile'],
    [/^\/groups\/?$/, 'groups'],
    [/^\/courses\/(\d+)\/?$/, 'course-home'],
    [/^\/courses\/(\d+)\/assignments\/syllabus/, 'syllabus'],
    [/^\/courses\/(\d+)\/assignments\/(\d+)/, 'assignment'],
    [/^\/courses\/(\d+)\/assignments/, 'assignments'],
    [/^\/courses\/(\d+)\/discussion_topics\/(\d+)/, 'discussion'],
    [/^\/courses\/(\d+)\/discussion_topics/, 'discussions'],
    [/^\/courses\/(\d+)\/announcements/, 'announcements'],
    [/^\/courses\/(\d+)\/modules\/items\/(\d+)/, 'module-item'],
    [/^\/courses\/(\d+)\/modules/, 'modules'],
    [/^\/courses\/(\d+)\/grades/, 'grades'],
    [/^\/courses\/(\d+)\/pages\/([^/?#]+)/, 'page'],
    [/^\/courses\/(\d+)\/pages/, 'pages'],
    [/^\/courses\/(\d+)\/wiki/, 'page'],
    [/^\/courses\/(\d+)\/quizzes\/(\d+)/, 'quiz'],
    [/^\/courses\/(\d+)\/quizzes/, 'quizzes'],
    [/^\/courses\/(\d+)\/files/, 'files'],
    [/^\/courses\/(\d+)\/users/, 'people'],
    [/^\/courses\/(\d+)\/external_tools\/(\d+)/, 'external-tool'],
    [/^\/courses\/(\d+)\/announcements\/?$/, 'announcements'],
    [/^\/courses\/(\d+)\//, 'course-other'],
  ];

  function describe() {
    const path = location.pathname.replace(/\/+$/, '') || '/';
    let kind = 'other';
    let courseId = null;
    let itemId = null;
    for (const [re, k] of ROUTES) {
      const m = path.match(re);
      if (m) {
        kind = k;
        courseId = m[1] || null;
        itemId = m[2] || null;
        break;
      }
    }
    const env = BCV.page?.env || readEnv();
    if (!courseId && env.COURSE_ID) courseId = String(env.COURSE_ID);
    if (!courseId && typeof env.context_asset_string === 'string' && env.context_asset_string.startsWith('course_')) {
      courseId = env.context_asset_string.slice(7);
    }
    const courseName = env.COURSE?.name || env.course_name || document.querySelector('#breadcrumbs li:nth-child(2) a span, #breadcrumbs li:nth-child(2) a')?.textContent?.trim() || null;
    const isDiscussionAnnouncement = kind === 'discussion' && /announcement/i.test(document.title);
    return {
      origin: location.origin,
      path,
      url: location.href,
      kind: isDiscussionAnnouncement ? 'announcement' : kind,
      courseId,
      itemId,
      env,
      courseName,
      title: (document.title || '').replace(/\s*[:|-]\s*[^:|-]*$/, '').trim() || document.title,
      isCanvas: isCanvasPage(),
      userId: env.current_user_id ? String(env.current_user_id) : null,
      userName: env.current_user?.display_name || null,
    };
  }

  function refresh() {
    BCV.page = describe();
    return BCV.page;
  }

  // ---- content extraction for the assistant -----------------------------

  function selectedText() {
    const sel = window.getSelection?.();
    const t = sel ? sel.toString().trim() : '';
    return t.length > 3 ? t : '';
  }

  function mainContentText(limit = MAX_CONTEXT_CHARS) {
    const root = document.querySelector('#content') || document.querySelector('main') || document.body;
    return elementText(root, limit);
  }

  function pointsLine(a) {
    return a.points_possible != null ? `${a.points_possible} points` : 'ungraded';
  }

  function fmt(d) {
    return d ? new Date(d).toLocaleString() : 'no date';
  }

  function rubricText(rubric, assessment) {
    if (!Array.isArray(rubric) || !rubric.length) return '';
    const lines = ['Rubric:'];
    for (const crit of rubric) {
      const got = assessment?.[crit.id];
      const rating = got && crit.ratings?.find((r) => r.id === got.rating_id);
      let line = `- ${crit.description}${crit.long_description ? ` — ${htmlToText(crit.long_description, 400)}` : ''} (${crit.points} pts)`;
      if (got) line += ` → scored ${got.points ?? '?'}${rating ? `: "${rating.description}"` : ''}${got.comments ? ` — comment: ${got.comments}` : ''}`;
      else if (crit.ratings?.length) line += ` — levels: ${crit.ratings.map((r) => `${r.description} (${r.points})`).join('; ')}`;
      lines.push(line);
    }
    return lines.join('\n');
  }

  function submissionText(sub) {
    if (!sub) return '';
    const lines = ['My submission:'];
    lines.push(`- status: ${sub.workflow_state}${sub.late ? ' (late)' : ''}${sub.missing ? ' (missing)' : ''}${sub.excused ? ' (excused)' : ''}`);
    if (sub.submitted_at) lines.push(`- submitted: ${fmt(sub.submitted_at)}`);
    if (sub.score != null) lines.push(`- score: ${sub.score}${sub.grade != null ? ` (grade ${sub.grade})` : ''}`);
    if (sub.attempt) lines.push(`- attempt: ${sub.attempt}`);
    if (Array.isArray(sub.submission_comments) && sub.submission_comments.length) {
      lines.push('- instructor/peer comments:');
      for (const c of sub.submission_comments.slice(-15)) {
        lines.push(`  • ${c.author_name || 'Someone'} (${fmt(c.created_at)}): ${htmlToText(c.comment, 1500)}`);
      }
    }
    if (sub.body) lines.push(`- submission text: ${htmlToText(sub.body, 4000)}`);
    return lines.join('\n');
  }

  async function assignmentContext(page) {
    const { courseId, itemId } = page;
    const C = BCV.canvas;
    const [assignment, submission] = await Promise.all([
      C.get(`/api/v1/courses/${courseId}/assignments/${itemId}`, { params: { include: ['submission'] } }),
      C.get(`/api/v1/courses/${courseId}/assignments/${itemId}/submissions/self`, {
        params: { include: ['rubric_assessment', 'submission_comments'] },
      }).catch(() => null),
    ]);
    const parts = [
      `Assignment: ${assignment.name}`,
      `Course: ${page.courseName || courseId}`,
      `Due: ${fmt(assignment.due_at)}${assignment.lock_at ? ` (locks ${fmt(assignment.lock_at)})` : ''}`,
      `Worth: ${pointsLine(assignment)}${assignment.grading_type ? `, graded as ${assignment.grading_type}` : ''}`,
      `Submission types: ${(assignment.submission_types || []).join(', ') || 'none'}${assignment.allowed_attempts > 0 ? `, ${assignment.allowed_attempts} attempts allowed` : ''}`,
      '',
      'Description:',
      htmlToText(assignment.description, 30000) || '(no description)',
    ];
    const rubric = rubricText(assignment.rubric, submission?.rubric_assessment);
    if (rubric) parts.push('', rubric);
    const sub = submissionText(submission);
    if (sub) parts.push('', sub);
    return { label: assignment.name, text: parts.join('\n'), graded: submission?.score != null || !!submission?.submission_comments?.length };
  }

  async function discussionContext(page) {
    const { courseId, itemId } = page;
    const C = BCV.canvas;
    const topic = await C.get(`/api/v1/courses/${courseId}/discussion_topics/${itemId}`);
    let view = null;
    try {
      view = await C.get(`/api/v1/courses/${courseId}/discussion_topics/${itemId}/view`);
    } catch {
      view = null;
    }
    const parts = [
      `${topic.is_announcement ? 'Announcement' : 'Discussion'}: ${topic.title}`,
      `Course: ${page.courseName || courseId}`,
      `Posted by: ${topic.author?.display_name || 'unknown'} on ${fmt(topic.posted_at)}`,
    ];
    if (topic.assignment?.due_at || topic.due_at) parts.push(`Due: ${fmt(topic.assignment?.due_at || topic.due_at)}`);
    if (topic.assignment?.points_possible != null) parts.push(`Worth: ${topic.assignment.points_possible} points`);
    parts.push('', 'Prompt:', htmlToText(topic.message, 20000) || '(empty)');
    if (view) {
      const people = new Map((view.participants || []).map((p) => [String(p.id), p.display_name]));
      const entries = [];
      const walk = (list, depth) => {
        for (const e of list || []) {
          if (entries.length >= 40) return;
          if (e.deleted) continue;
          const who = people.get(String(e.user_id)) || 'Student';
          entries.push(`${'  '.repeat(depth)}- ${who} (${fmt(e.created_at)}): ${htmlToText(e.message, 1500)}`);
          if (e.replies) walk(e.replies, depth + 1);
        }
      };
      walk(view.view, 0);
      if (entries.length) parts.push('', `Replies so far (${entries.length}${entries.length >= 40 ? '+, truncated' : ''}):`, ...entries);
    }
    return { label: topic.title, text: parts.join('\n') };
  }

  async function pageContext(page) {
    const { courseId } = page;
    const slug = page.itemId || location.pathname.split('/pages/')[1]?.split(/[?#]/)[0];
    const C = BCV.canvas;
    let p = null;
    if (slug) p = await C.get(`/api/v1/courses/${courseId}/pages/${slug}`).catch(() => null);
    if (!p) return null;
    return { label: p.title, text: [`Page: ${p.title}`, `Course: ${page.courseName || courseId}`, `Updated: ${fmt(p.updated_at)}`, '', htmlToText(p.body, 50000)].join('\n') };
  }

  async function quizContext(page) {
    const { courseId, itemId } = page;
    const q = await BCV.canvas.get(`/api/v1/courses/${courseId}/quizzes/${itemId}`);
    return {
      label: q.title,
      text: [
        `Quiz: ${q.title}`,
        `Course: ${page.courseName || courseId}`,
        `Due: ${fmt(q.due_at)}${q.lock_at ? ` (locks ${fmt(q.lock_at)})` : ''}`,
        `Questions: ${q.question_count ?? '?'} · Points: ${q.points_possible ?? '?'} · Time limit: ${q.time_limit ? q.time_limit + ' min' : 'none'} · Attempts: ${q.allowed_attempts === -1 ? 'unlimited' : q.allowed_attempts ?? 1}`,
        '',
        'Description:',
        htmlToText(q.description, 20000) || '(none)',
      ].join('\n'),
    };
  }

  async function modulesContext(page) {
    const { courseId } = page;
    const mods = await BCV.canvas.get(`/api/v1/courses/${courseId}/modules`, { params: { include: ['items'], per_page: 50 }, all: true });
    const lines = [`Course modules for ${page.courseName || courseId}:`];
    for (const m of mods) {
      lines.push(`\n## ${m.name}${m.unlock_at ? ` (unlocks ${fmt(m.unlock_at)})` : ''}${m.state ? ` [${m.state}]` : ''}`);
      for (const it of m.items || []) {
        if (it.type === 'SubHeader') {
          lines.push(`  ${it.title}:`);
          continue;
        }
        const due = it.content_details?.due_at ? ` — due ${fmt(it.content_details.due_at)}` : '';
        const pts = it.content_details?.points_possible != null ? ` (${it.content_details.points_possible} pts)` : '';
        const done = it.completion_requirement ? (it.completion_requirement.completed ? ' ✓' : ' ○') : '';
        lines.push(`  - [${it.type}] ${it.title}${pts}${due}${done}`);
      }
    }
    return { label: 'Modules', text: lines.join('\n') };
  }

  async function gradesContext(page) {
    const { courseId } = page;
    const C = BCV.canvas;
    const [enrollments, submissions] = await Promise.all([
      C.get(`/api/v1/courses/${courseId}/enrollments`, { params: { user_id: 'self' } }).catch(() => []),
      C.get(`/api/v1/courses/${courseId}/students/submissions`, {
        params: { 'student_ids[]': 'self', include: ['assignment', 'submission_comments', 'rubric_assessment'], per_page: 100 },
        all: true,
        maxPages: 3,
      }).catch(() => []),
    ]);
    const lines = [`Grades for ${page.courseName || courseId}:`];
    const enr = (enrollments || []).find((e) => e.grades);
    if (enr?.grades) {
      lines.push(`Current score: ${enr.grades.current_score ?? '?'}%${enr.grades.current_grade ? ` (${enr.grades.current_grade})` : ''} · Final so far: ${enr.grades.final_score ?? '?'}%`);
    }
    for (const s of submissions || []) {
      const a = s.assignment || {};
      const status = s.workflow_state === 'graded' ? `score ${s.score ?? '-'} / ${a.points_possible ?? '?'}` : s.workflow_state;
      lines.push(`\n- ${a.name || 'Assignment'} (due ${fmt(a.due_at)}): ${status}${s.late ? ', late' : ''}${s.missing ? ', missing' : ''}`);
      if (s.submission_comments?.length) {
        for (const c of s.submission_comments.slice(-5)) lines.push(`    comment from ${c.author_name || 'someone'}: ${htmlToText(c.comment, 800)}`);
      }
      if (s.rubric_assessment && a.rubric) {
        const r = rubricText(a.rubric, s.rubric_assessment).split('\n').slice(1);
        lines.push(...r.map((l) => '    ' + l));
      }
    }
    return { label: 'Grades', text: lines.join('\n') };
  }

  async function syllabusContext(page) {
    const c = await BCV.canvas.get(`/api/v1/courses/${page.courseId}`, { params: { include: ['syllabus_body'] } });
    return { label: `${c.name} syllabus`, text: [`Syllabus for ${c.name}`, '', htmlToText(c.syllabus_body, 50000) || '(empty)'].join('\n') };
  }

  async function dashboardContext() {
    const C = BCV.canvas;
    const [items, cards] = await Promise.all([C.plannerItems(21).catch(() => []), C.dashboardCards().catch(() => [])]);
    const lines = ['My courses:'];
    for (const c of cards || []) lines.push(`- ${c.shortName || c.originalName} (${c.courseCode || ''})`);
    lines.push('', 'Upcoming and recent items from the Canvas planner. "due" means graded work with a real due date; "to-do date" means an instructor-scheduled item (ungraded discussion, page, note) or event that is not graded:');
    for (const it of items || []) {
      const c = BCV.dueData?.classify ? BCV.dueData.classify(it) : null;
      const type = c ? c.typeLabel : it.plannable_type;
      const when = c ? (c.isDue ? `due ${fmt(c.due)}` : `to-do date ${fmt(c.due)}`) : `date ${fmt(it.plannable?.due_at || it.plannable_date)}`;
      const done = it.planner_override?.marked_complete || it.submissions?.submitted || it.submissions?.graded;
      lines.push(`- ${it.context_name || ''}: ${it.plannable?.title || it.plannable_type} — ${type}, ${when}${it.plannable?.points_possible != null ? `, ${it.plannable.points_possible} pts` : ''}${done ? ' [done]' : ''}${it.submissions?.missing ? ' [missing]' : ''}`);
    }
    return { label: 'Dashboard', text: lines.join('\n') };
  }

  /** Build the context block for the assistant. Cached per URL. */
  const contextCache = new Map();
  async function buildContext({ force = false } = {}) {
    const page = refresh();
    const key = page.url;
    if (!force && contextCache.has(key)) return contextCache.get(key);
    const task = (async () => {
      let ctx = null;
      try {
        switch (page.kind) {
          case 'assignment': ctx = await assignmentContext(page); break;
          case 'discussion':
          case 'announcement': ctx = await discussionContext(page); break;
          case 'page': ctx = await pageContext(page); break;
          case 'quiz': ctx = await quizContext(page); break;
          case 'modules': ctx = await modulesContext(page); break;
          case 'grades': ctx = await gradesContext(page); break;
          case 'syllabus': ctx = await syllabusContext(page); break;
          case 'dashboard': ctx = await dashboardContext(page); break;
          default: ctx = null;
        }
      } catch (e) {
        ctx = null;
      }
      if (!ctx) ctx = { label: page.title || 'This page', text: mainContentText() };
      else if (ctx.text.length < 200) ctx.text += '\n\nVisible page text:\n' + mainContentText(20000);
      if (ctx.text.length > MAX_CONTEXT_CHARS) ctx.text = ctx.text.slice(0, MAX_CONTEXT_CHARS) + '\n…[truncated]';
      return { ...ctx, kind: page.kind, url: page.url, title: page.title, course: page.courseName };
    })();
    contextCache.set(key, task);
    task.catch(() => contextCache.delete(key));
    return task;
  }

  BCV.pageContext = { readEnv, describe, refresh, buildContext, selectedText, mainContentText, isCanvasPage, MAX_CONTEXT_CHARS };
  refresh();
})();
