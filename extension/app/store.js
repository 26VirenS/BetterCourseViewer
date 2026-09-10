/* Data layer for the redesigned interface. Every number on screen traces
 * back to a Canvas API response field loaded here; nothing is estimated.
 * Loaders cache in extension storage (see lib/canvas-api.js) so screens
 * paint from cache and refresh in the background. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const C = BCV.canvas;
  const U = BCV.ui;
  const IC = BCV.IC;
  const api = BCV.api;
  const MIN = 60e3;

  /** Canvas's page ENV. Content scripts cannot see page globals, so it is
   *  parsed out of the inline <script> Canvas emits in the document head. */
  let envCache = null;
  function env() {
    if (envCache) return envCache;
    envCache = {};
    try {
      for (const s of document.querySelectorAll('script:not([src])')) {
        const text = s.textContent || '';
        const idx = text.search(/\bENV\s*=\s*\{/);
        if (idx < 0) continue;
        const start = text.indexOf('{', idx);
        let depth = 0, inStr = false, esc = false, end = -1;
        for (let i = start; i < text.length; i++) {
          const ch = text[i];
          if (inStr) {
            if (esc) esc = false;
            else if (ch === '\\') esc = true;
            else if (ch === '"') inStr = false;
          } else if (ch === '"') inStr = true;
          else if (ch === '{') depth++;
          else if (ch === '}' && --depth === 0) {
            end = i;
            break;
          }
        }
        if (end > start) {
          envCache = JSON.parse(text.slice(start, end + 1));
          break;
        }
      }
    } catch {
      envCache = {};
    }
    return envCache;
  }

  // ---- local per-site preferences (things Canvas has no API for) ---------------
  const prefKey = `prefs:${location.host}`;
  let prefsCache = null;
  async function prefs() {
    if (prefsCache) return prefsCache;
    try {
      const r = await api.storage.local.get(prefKey);
      prefsCache = r[prefKey] || {};
    } catch {
      prefsCache = {};
    }
    return prefsCache;
  }
  async function pref(key, fallback = null) {
    const p = await prefs();
    return p[key] === undefined ? fallback : p[key];
  }
  async function setPref(key, value) {
    const p = await prefs();
    p[key] = value;
    prefsCache = p;
    try {
      await api.storage.local.set({ [prefKey]: p });
    } catch {
      /* ignore */
    }
  }

  // ---- user + site --------------------------------------------------------------
  function me({ force = false } = {}) {
    return C.cached('me', 60 * MIN, async () => {
      try {
        const u = await C.get('/api/v1/users/self');
        return { id: String(u.id), name: u.name || u.short_name, shortName: u.short_name || u.name, avatar: u.avatar_url || null, pronouns: u.pronouns || null };
      } catch {
        const e = env();
        const cu = e.current_user || {};
        return { id: String(e.current_user_id || ''), name: cu.display_name || 'Account', shortName: cu.display_name || 'Account', avatar: cu.avatar_image_url || null, pronouns: null };
      }
    }, { force });
  }

  function account() {
    return C.cached('account', 24 * 60 * MIN, async () => {
      const id = env().DOMAIN_ROOT_ACCOUNT_ID;
      if (!id) return null;
      try {
        const a = await C.get(`/api/v1/accounts/${id}`);
        return a?.name ? { id: String(a.id), name: a.name } : null;
      } catch {
        return null;
      }
    });
  }

  // ---- courses ------------------------------------------------------------------------
  function colors({ force = false } = {}) {
    return C.cached('colors', 60 * MIN, async () => {
      try {
        const r = await C.get('/api/v1/users/self/colors');
        return r?.custom_colors || {};
      } catch {
        return {};
      }
    }, { force });
  }

  function rawCourses({ force = false } = {}) {
    return C.cached('courses:all', 15 * MIN, () =>
      C.get('/api/v1/courses', {
        params: { per_page: 100, include: ['term', 'favorites', 'total_scores', 'teachers', 'sections', 'course_image'] },
        all: true,
        maxPages: 5,
      }), { force });
  }

  function cards({ force = false } = {}) {
    return C.cached('cards', 15 * MIN, () => C.get('/api/v1/dashboard/dashboard_cards'), { force });
  }

  const now = () => new Date();
  function courseState(c, t = now()) {
    const term = c.term || {};
    const enr = (c.enrollments || [])[0] || {};
    const start = U.parse(c.start_at || term.start_at);
    const end = U.parse(c.end_at || term.end_at);
    if (c.workflow_state === 'completed' || enr.enrollment_state === 'completed' || (end && end < t && c.restrict_enrollments_to_course_dates !== false && end < t)) return 'past';
    if (enr.enrollment_state === 'invited' || enr.enrollment_state === 'creation_pending' || (start && start > t)) return 'future';
    return 'current';
  }

  /** All courses, decorated with colour, palette, favourite flag and state. */
  async function courses({ force = false } = {}) {
    const [list, cols, dark] = await Promise.all([rawCourses({ force }), colors({ force }), Promise.resolve(BCV.early?.isDark?.() ?? false)]);
    const seen = new Set();
    const out = [];
    let fallbackIdx = 0;
    for (const c of list || []) {
      const id = String(c.id);
      if (seen.has(id)) continue;
      seen.add(id);
      const enr = (c.enrollments || [])[0] || {};
      let color = cols[`course_${id}`];
      if (!color) color = U.FALLBACK_COLORS[fallbackIdx++ % U.FALLBACK_COLORS.length];
      out.push({
        id,
        raw: c,
        name: c.name || c.course_code || `Course ${id}`,
        originalName: c.original_name || c.name,
        nickname: c.original_name ? c.name : null,
        code: c.course_code || c.name,
        term: c.term?.name || '',
        termId: c.term?.id ? String(c.term.id) : null,
        favorite: !!c.is_favorite,
        state: courseState(c),
        role: roleLabel(enr.type || enr.role),
        score: enr.computed_current_score ?? null,
        grade: enr.computed_current_grade ?? null,
        finalScore: enr.computed_final_score ?? null,
        teachers: (c.teachers || []).map((t) => t.display_name).filter(Boolean),
        sections: (c.sections || []).map((s) => s.name).filter(Boolean),
        image: c.image_download_url || null,
        defaultView: c.default_view || 'wiki',
        weighted: !!c.apply_assignment_group_weights,
        color,
        palette: U.palette(color, dark),
        url: `/courses/${id}`,
      });
    }
    return out;
  }
  function roleLabel(type) {
    const t = String(type || '').toLowerCase();
    if (t.includes('teacher')) return 'Teacher';
    if (t.includes('ta')) return 'TA';
    if (t.includes('designer')) return 'Designer';
    if (t.includes('observer')) return 'Observer';
    return 'Student';
  }

  /** Favourite courses in dashboard order (falls back to all current courses). */
  async function favorites({ force = false } = {}) {
    const [all, cardList] = await Promise.all([courses({ force }), cards({ force }).catch(() => [])]);
    const byId = new Map(all.map((c) => [c.id, c]));
    const order = (cardList || []).map((k) => String(k.id));
    const favs = order.map((id) => byId.get(id)).filter(Boolean).map((c) => {
      const k = (cardList || []).find((x) => String(x.id) === c.id) || {};
      return { ...c, favorite: true, shortName: k.shortName || c.name, subtitle: k.subtitle || c.sections[0] || '', cardTerm: k.term || c.term, links: k.links || [] };
    });
    if (favs.length) return favs;
    return all.filter((c) => c.state === 'current').map((c) => ({ ...c, shortName: c.name, subtitle: c.sections[0] || '', cardTerm: c.term, links: [] }));
  }

  async function setFavorite(courseId, on) {
    if (on) await C.post(`/api/v1/users/self/favorites/courses/${courseId}`, {});
    else await C.del(`/api/v1/users/self/favorites/courses/${courseId}`);
    await Promise.all([C.invalidate('courses:all'), C.invalidate('cards')]);
  }

  /** Term to show under the site name: the term shared by most favourite courses. */
  async function currentTerm() {
    const favs = await favorites().catch(() => []);
    const counts = new Map();
    for (const c of favs) if (c.term) counts.set(c.term, (counts.get(c.term) || 0) + 1);
    let best = '';
    let n = 0;
    for (const [t, k] of counts) if (k > n) { best = t; n = k; }
    return best;
  }

  // ---- dashboard view (stored on the Canvas user) -------------------------------------
  const VIEW_MAP = { cards: 'cards', planner: 'list', activity: 'activity' };
  const VIEW_BACK = { cards: 'cards', list: 'planner', activity: 'activity' };
  async function dashboardView() {
    const e = env();
    const fromEnv = e.PREFERENCES?.dashboard_view;
    if (fromEnv && VIEW_MAP[fromEnv]) return VIEW_MAP[fromEnv];
    const local = await pref('dashboardView');
    if (local) return local;
    try {
      const r = await C.get('/dashboard/view');
      if (r?.dashboard_view && VIEW_MAP[r.dashboard_view]) return VIEW_MAP[r.dashboard_view];
    } catch {
      /* not every Canvas exposes it */
    }
    return 'list';
  }
  async function setDashboardView(view) {
    await setPref('dashboardView', view);
    try {
      await C.put('/dashboard/view', { dashboard_view: VIEW_BACK[view] || 'planner' });
      if (env().PREFERENCES) env().PREFERENCES.dashboard_view = VIEW_BACK[view];
    } catch {
      /* stored locally anyway */
    }
  }

  // ---- planner / to do ---------------------------------------------------------------------
  function isoDays(n) {
    return U.addDays(U.startOfDay(now()), n).toISOString();
  }
  function rawPlanner(days = 21, { force = false } = {}) {
    return C.cached(`planner:${days}`, 3 * MIN, () =>
      C.get('/api/v1/planner/items', { params: { start_date: isoDays(-7), end_date: isoDays(days), per_page: 100 }, all: true, maxPages: 5 }), { force });
  }

  /** Normalise a planner item: what it is, whether it is actually due, and its state. */
  function classify(item, courseMap = null) {
    const type = item.plannable_type;
    const p = item.plannable || {};
    let kind = 'Assignment', icon = IC.doc, isDue = true;
    if (type === 'quiz') { kind = 'Quiz'; icon = IC.bolt; }
    else if (type === 'discussion_topic') {
      const graded = !!(p.assignment_id || p.due_at || (item.submissions && typeof item.submissions === 'object'));
      kind = graded ? 'Discussion · graded' : 'Discussion';
      icon = IC.disc;
      isDue = graded;
    } else if (type === 'wiki_page') { kind = 'Page'; icon = IC.page; isDue = false; }
    else if (type === 'calendar_event') { kind = 'Event'; icon = IC.cal; isDue = false; }
    else if (type === 'planner_note') { kind = 'Note'; icon = IC.doc; isDue = false; }
    else if (type === 'announcement') { kind = 'Announcement'; icon = IC.bell; isDue = false; }
    else if (type === 'assessment_request') { kind = 'Peer review'; icon = IC.people; }
    else if (type === 'assignment') { isDue = !!(p.due_at || item.plannable_date); }
    const subs = item.submissions && typeof item.submissions === 'object' ? item.submissions : {};
    const date = U.parse(p.due_at || p.todo_date || p.start_at || item.plannable_date);
    const courseId = item.course_id ? String(item.course_id) : null;
    const course = courseId && courseMap ? courseMap.get(courseId) : null;
    return {
      id: `${type}:${item.plannable_id}`,
      raw: item,
      type,
      kind,
      icon,
      isDue,
      date,
      title: p.title || p.name || 'Untitled',
      points: p.points_possible ?? null,
      courseId,
      courseName: course?.name || item.context_name || '',
      course,
      submitted: !!(subs.submitted || subs.graded),
      graded: !!subs.graded,
      missing: !!subs.missing,
      late: !!subs.late,
      excused: !!subs.excused,
      complete: !!item.planner_override?.marked_complete,
      dismissed: !!item.planner_override?.dismissed,
      url: item.html_url || (courseId ? `/courses/${courseId}` : '/'),
    };
  }

  async function planner({ force = false, days = 21 } = {}) {
    const [items, cs] = await Promise.all([rawPlanner(days, { force }), courses().catch(() => [])]);
    const map = new Map(cs.map((c) => [c.id, c]));
    return (items || []).map((it) => classify(it, map)).filter((it) => it.date);
  }

  /** Items on the To Do list: not done, not dismissed, from today through the next 7 days. */
  async function todo(opts = {}) {
    const items = await planner(opts);
    const t = now();
    const end = U.addDays(U.startOfDay(t), 8);
    return items.filter((it) => !it.complete && !it.dismissed && !it.submitted && it.date >= U.startOfDay(t) && it.date < end && it.type !== 'announcement');
  }

  async function setComplete(item, complete) {
    const ov = item.raw.planner_override;
    if (ov?.id) await C.put(`/api/v1/planner/overrides/${ov.id}`, { marked_complete: complete });
    else await C.post('/api/v1/planner/overrides', { plannable_type: item.type, plannable_id: item.raw.plannable_id, marked_complete: complete });
    await invalidatePlanner();
  }
  async function dismiss(item) {
    const ov = item.raw.planner_override;
    if (ov?.id) await C.put(`/api/v1/planner/overrides/${ov.id}`, { dismissed: true });
    else await C.post('/api/v1/planner/overrides', { plannable_type: item.type, plannable_id: item.raw.plannable_id, dismissed: true });
    await invalidatePlanner();
  }
  async function invalidatePlanner() {
    await Promise.all([C.invalidate('planner:21'), C.invalidate('planner:14'), C.invalidate('planner:60')]);
  }

  // ---- activity + counts ------------------------------------------------------------------
  function activity({ force = false } = {}) {
    return C.cached('activity', 3 * MIN, () => C.get('/api/v1/users/self/activity_stream', { params: { per_page: 40, only_active_courses: true } }), { force });
  }
  function activitySummary({ force = false } = {}) {
    return C.cached('activity:summary', 3 * MIN, () => C.get('/api/v1/users/self/activity_stream/summary', { params: { only_active_courses: true } }), { force });
  }
  function unreadCount({ force = false } = {}) {
    return C.cached('unread', 2 * MIN, async () => {
      try {
        const r = await C.get('/api/v1/conversations/unread_count');
        return Number(r?.unread_count) || 0;
      } catch {
        return 0;
      }
    }, { force });
  }

  // ---- groups ---------------------------------------------------------------------------------
  function groups({ force = false } = {}) {
    return C.cached('groups', 15 * MIN, () => C.get('/api/v1/users/self/groups', { params: { per_page: 50, include: ['group_category'] }, all: true }), { force });
  }

  // ---- calendar -------------------------------------------------------------------------------
  async function calendarContexts() {
    const [u, cs, gs] = await Promise.all([me(), courses(), groups().catch(() => [])]);
    const list = [{ code: `user_${u.id}`, name: u.name, color: '#0a84ff', kind: 'user' }];
    for (const c of cs) if (c.state !== 'past') list.push({ code: `course_${c.id}`, name: c.name, color: c.color, kind: 'course', courseId: c.id });
    for (const g of gs || []) list.push({ code: `group_${g.id}`, name: g.name, color: '#6b5f7a', kind: 'group' });
    return list;
  }
  async function selectedContexts(all) {
    const e = env();
    const stored = await pref('calendarContexts');
    let selected = stored || (Array.isArray(e.SELECTED_CONTEXT_CODES) ? e.SELECTED_CONTEXT_CODES : null);
    if (!selected) selected = all.slice(0, 10).map((c) => c.code);
    const codes = new Set(all.map((c) => c.code));
    return selected.filter((c) => codes.has(c));
  }
  async function setSelectedContexts(codes) {
    await setPref('calendarContexts', codes);
  }
  function calendarEvents(start, end, codes, { force = false } = {}) {
    const s = U.startOfDay(start).toISOString();
    const e = U.addDays(U.startOfDay(end), 1).toISOString();
    const key = `cal:${s.slice(0, 10)}:${e.slice(0, 10)}:${codes.join(',')}`;
    return C.cached(key, 5 * MIN, async () => {
      if (!codes.length) return [];
      const params = { start_date: s, end_date: e, 'context_codes[]': codes, per_page: 100, include: ['submission'] };
      const [ev, as] = await Promise.all([
        C.get('/api/v1/calendar_events', { params: { ...params, type: 'event' }, all: true, maxPages: 5 }).catch(() => []),
        C.get('/api/v1/calendar_events', { params: { ...params, type: 'assignment' }, all: true, maxPages: 5 }).catch(() => []),
      ]);
      return [...(ev || []), ...(as || [])];
    }, { force });
  }

  // ---- inbox ----------------------------------------------------------------------------------
  function conversations({ scope = 'inbox', filter = null, force = false } = {}) {
    const params = { per_page: 50, include_all_conversation_ids: false };
    if (scope && scope !== 'inbox') params.scope = scope;
    if (filter) params['filter[]'] = filter;
    return C.cached(`conv:${scope}:${filter || ''}`, 2 * MIN, () => C.get('/api/v1/conversations', { params }), { force });
  }
  function conversation(id, { force = true } = {}) {
    return C.cached(`conv:${id}`, MIN, () => C.get(`/api/v1/conversations/${id}`), { force });
  }
  async function invalidateInbox() {
    const stored = await api.storage.local.get(null).catch(() => ({}));
    const keys = Object.keys(stored).filter((k) => k.startsWith(`cache:${location.host}:conv:`) || k.endsWith(':unread'));
    await Promise.all(keys.map((k) => C.invalidate(k.replace(`cache:${location.host}:`, ''))));
  }
  async function markRead(id) {
    await C.put(`/api/v1/conversations/${id}`, { conversation: { workflow_state: 'read' } });
    await invalidateInbox();
  }
  async function setStarred(id, starred) {
    await C.put(`/api/v1/conversations/${id}`, { conversation: { starred } });
    await invalidateInbox();
  }
  async function replyTo(id, body) {
    const r = await C.post(`/api/v1/conversations/${id}/add_message`, { body });
    await invalidateInbox();
    return r;
  }
  async function compose({ recipients, subject, body, contextCode }) {
    const payload = { recipients, subject, body, group_conversation: recipients.length > 1 };
    if (contextCode) payload.context_code = contextCode;
    const r = await C.post('/api/v1/conversations', payload);
    await invalidateInbox();
    return r;
  }
  function searchRecipients(q, context) {
    const params = { search: q, per_page: 20 };
    if (context) params.context = context;
    return C.get('/api/v1/search/recipients', { params });
  }

  // ---- course -------------------------------------------------------------------------------------
  async function course(id, { force = false } = {}) {
    const list = await courses({ force });
    const found = list.find((c) => c.id === String(id));
    if (found) return found;
    const c = await C.cached(`course:${id}`, 15 * MIN, () => C.get(`/api/v1/courses/${id}`, { params: { include: ['term', 'teachers', 'total_scores', 'sections', 'course_image'] } }), { force });
    const cols = await colors();
    const color = cols[`course_${id}`] || U.FALLBACK_COLORS[0];
    const enr = (c.enrollments || [])[0] || {};
    return {
      id: String(c.id), raw: c, name: c.name, originalName: c.original_name || c.name, nickname: null, code: c.course_code, term: c.term?.name || '', favorite: !!c.is_favorite,
      state: courseState(c), role: roleLabel(enr.type), score: enr.computed_current_score ?? null, grade: enr.computed_current_grade ?? null, teachers: (c.teachers || []).map((t) => t.display_name),
      sections: (c.sections || []).map((s) => s.name), image: c.image_download_url || null, defaultView: c.default_view || 'wiki', weighted: !!c.apply_assignment_group_weights,
      color, palette: U.palette(color, BCV.early?.isDark?.() ?? false), url: `/courses/${id}`,
    };
  }
  function tabs(id, { force = false } = {}) {
    return C.cached(`tabs:${id}`, 30 * MIN, () => C.get(`/api/v1/courses/${id}/tabs`), { force });
  }
  function frontPage(id, { force = false } = {}) {
    return C.cached(`front:${id}`, 10 * MIN, () => C.get(`/api/v1/courses/${id}/front_page`).catch(() => null), { force });
  }
  function syllabus(id, { force = false } = {}) {
    return C.cached(`syllabus:${id}`, 10 * MIN, () => C.get(`/api/v1/courses/${id}`, { params: { include: ['syllabus_body'] } }).then((c) => c?.syllabus_body || ''), { force });
  }
  function courseTodo(id, { force = false } = {}) {
    return C.cached(`ctodo:${id}`, 3 * MIN, () => C.get(`/api/v1/courses/${id}/todo`, { params: { per_page: 20 } }).catch(() => []), { force });
  }
  async function ignoreTodo(item, courseId) {
    const url = item.ignore || item.ignore_permanently;
    if (!url) return;
    const path = url.replace(/^https?:\/\/[^/]+/, '');
    await C.del(path);
    await C.invalidate(`ctodo:${courseId}`);
  }
  function courseStream(id, { force = false } = {}) {
    return C.cached(`cstream:${id}`, 3 * MIN, () => C.get(`/api/v1/courses/${id}/activity_stream`, { params: { per_page: 40 } }), { force });
  }
  function assignments(id, { force = false } = {}) {
    return C.cached(`assignments:${id}`, 10 * MIN, () =>
      C.get(`/api/v1/courses/${id}/assignments`, { params: { per_page: 100, include: ['submission', 'all_dates'], order_by: 'due_at' }, all: true, maxPages: 4 }), { force });
  }
  function assignment(id, aid, { force = false } = {}) {
    return C.cached(`assignment:${id}:${aid}`, 5 * MIN, () =>
      C.get(`/api/v1/courses/${id}/assignments/${aid}`, { params: { include: ['submission', 'score_statistics', 'can_submit'] } }), { force });
  }
  function submission(id, aid, { force = false } = {}) {
    return C.cached(`submission:${id}:${aid}`, 5 * MIN, () =>
      C.get(`/api/v1/courses/${id}/assignments/${aid}/submissions/self`, { params: { include: ['submission_comments', 'rubric_assessment'] } }).catch(() => null), { force });
  }
  function assignmentGroups(id, { force = false } = {}) {
    return C.cached(`agroups:${id}`, 10 * MIN, () =>
      C.get(`/api/v1/courses/${id}/assignment_groups`, { params: { per_page: 50, include: ['assignments', 'submission'], exclude_assignment_submission_types: ['wiki_page'] }, all: true, maxPages: 3 }), { force });
  }
  /** Submitted ÷ total assignments for the progress bars. */
  async function progress(id) {
    try {
      const list = await assignments(id);
      const total = (list || []).length;
      const done = (list || []).filter((a) => ['submitted', 'graded', 'pending_review'].includes(a.submission?.workflow_state) && (a.submission?.submitted_at || a.submission?.workflow_state === 'graded')).length;
      return { done, total };
    } catch {
      return { done: 0, total: 0 };
    }
  }
  function announcements(id, { force = false } = {}) {
    return C.cached(`ann:${id}`, 5 * MIN, () =>
      C.get(`/api/v1/courses/${id}/discussion_topics`, { params: { only_announcements: true, per_page: 50 }, all: true, maxPages: 2 }), { force });
  }
  function discussions(id, { force = false } = {}) {
    return C.cached(`disc:${id}`, 5 * MIN, () =>
      C.get(`/api/v1/courses/${id}/discussion_topics`, { params: { per_page: 50, order_by: 'recent_activity', include: ['all_dates'] }, all: true, maxPages: 3 }), { force });
  }
  function discussion(id, tid, { force = false } = {}) {
    return C.cached(`disc:${id}:${tid}`, 3 * MIN, () => C.get(`/api/v1/courses/${id}/discussion_topics/${tid}`, { params: { include: ['all_dates'] } }), { force });
  }
  function discussionView(id, tid, { force = true } = {}) {
    return C.cached(`discview:${id}:${tid}`, MIN, () => C.get(`/api/v1/courses/${id}/discussion_topics/${tid}/view`).catch(() => null), { force });
  }
  async function postEntry(id, tid, message, parentId = null) {
    const r = parentId
      ? await C.post(`/api/v1/courses/${id}/discussion_topics/${tid}/entries/${parentId}/replies`, { message })
      : await C.post(`/api/v1/courses/${id}/discussion_topics/${tid}/entries`, { message });
    await Promise.all([C.invalidate(`discview:${id}:${tid}`), C.invalidate(`disc:${id}`), C.invalidate(`disc:${id}:${tid}`)]);
    return r;
  }
  async function markTopicRead(id, tid) {
    try {
      await C.put(`/api/v1/courses/${id}/discussion_topics/${tid}/read_all`, {});
      await Promise.all([C.invalidate(`disc:${id}`), C.invalidate(`ann:${id}`)]);
    } catch {
      /* ignore */
    }
  }
  function people(id, { force = false } = {}) {
    return C.cached(`people:${id}`, 15 * MIN, () =>
      C.get(`/api/v1/courses/${id}/users`, { params: { per_page: 100, include: ['enrollments', 'avatar_url', 'pronouns'], sort: 'username' }, all: true, maxPages: 5 }), { force });
  }
  function sections(id, { force = false } = {}) {
    return C.cached(`sections:${id}`, 30 * MIN, () => C.get(`/api/v1/courses/${id}/sections`, { params: { per_page: 100 } }).catch(() => []), { force });
  }
  function courseGroups(id, { force = false } = {}) {
    return C.cached(`cgroups:${id}`, 15 * MIN, () => C.get(`/api/v1/courses/${id}/groups`, { params: { per_page: 100, include: ['group_category'] } }).catch(() => []), { force });
  }
  function pages(id, { force = false } = {}) {
    return C.cached(`pages:${id}`, 10 * MIN, () =>
      C.get(`/api/v1/courses/${id}/pages`, { params: { per_page: 100, sort: 'title', published: true }, all: true, maxPages: 3 }), { force });
  }
  function page(id, slug, { force = false } = {}) {
    return C.cached(`page:${id}:${slug}`, 10 * MIN, () => C.get(`/api/v1/courses/${id}/pages/${encodeURIComponent(slug)}`), { force });
  }
  function rootFolder(id, { force = false } = {}) {
    return C.cached(`folder:root:${id}`, 15 * MIN, () => C.get(`/api/v1/courses/${id}/folders/root`), { force });
  }
  function folderContents(folderId, { force = false } = {}) {
    return C.cached(`folder:${folderId}`, 10 * MIN, async () => {
      const [folders, files] = await Promise.all([
        C.get(`/api/v1/folders/${folderId}/folders`, { params: { per_page: 100 }, all: true, maxPages: 2 }).catch(() => []),
        C.get(`/api/v1/folders/${folderId}/files`, { params: { per_page: 100, include: ['user'] }, all: true, maxPages: 3 }).catch(() => []),
      ]);
      return { folders: folders || [], files: files || [] };
    }, { force });
  }
  function folderByPath(id, path, { force = false } = {}) {
    return C.cached(`folder:path:${id}:${path}`, 10 * MIN, () =>
      C.get(`/api/v1/courses/${id}/folders/by_path/${path.split('/').map(encodeURIComponent).join('/')}`), { force });
  }
  function file(fileId, { force = false } = {}) {
    return C.cached(`file:${fileId}`, 10 * MIN, () => C.get(`/api/v1/files/${fileId}`), { force });
  }
  function quizzes(id, { force = false } = {}) {
    return C.cached(`quizzes:${id}`, 10 * MIN, () => C.get(`/api/v1/courses/${id}/quizzes`, { params: { per_page: 100 }, all: true, maxPages: 3 }), { force });
  }
  function quiz(id, qid, { force = false } = {}) {
    return C.cached(`quiz:${id}:${qid}`, 5 * MIN, () => C.get(`/api/v1/courses/${id}/quizzes/${qid}`), { force });
  }
  function quizSubmissions(id, qid, { force = false } = {}) {
    return C.cached(`quizsubs:${id}:${qid}`, 3 * MIN, () => C.get(`/api/v1/courses/${id}/quizzes/${qid}/submissions`).then((r) => r?.quiz_submissions || []).catch(() => []), { force });
  }
  function modules(id, { force = false } = {}) {
    return C.cached(`modules:${id}`, 10 * MIN, () =>
      C.get(`/api/v1/courses/${id}/modules`, { params: { per_page: 50, include: ['items', 'content_details'] }, all: true, maxPages: 4 }), { force });
  }

  // ---- grades model -------------------------------------------------------------------------------
  /** Build the grade picture: one ring per group that has graded, counted work. */
  function gradeModel(groupsRaw, courseInfo, whatIf, whatIfOn, dark) {
    const weighted = !!courseInfo.weighted;
    const rows = [];
    const groups = (groupsRaw || []).map((g) => ({ id: String(g.id), name: g.name, weight: Number(g.group_weight) || 0, position: g.position, rules: g.rules || {}, assignments: g.assignments || [] }));
    for (const g of groups) {
      for (const a of g.assignments) {
        if (a.published === false) continue;
        const s = a.submission || {};
        const graded = s.workflow_state === 'graded' && s.score !== null && s.score !== undefined && !s.excused;
        const key = String(a.id);
        const raw = whatIf?.[key];
        const hyp = whatIfOn && raw !== undefined && raw !== '' ? Number(raw) : null;
        const cleared = whatIfOn && raw === '';
        const effective = hyp !== null ? hyp : (cleared ? null : (graded ? Number(s.score) : null));
        let badgeText = '';
        if (a.omit_from_final_grade) badgeText = 'Not counted toward final grade';
        else if (s.excused) badgeText = 'Excused';
        else if (s.missing) badgeText = 'Missing';
        else if (s.late) badgeText = 'Late';
        rows.push({
          id: key, name: a.name, group: g.name, groupId: g.id, possible: Number(a.points_possible) || 0, earned: graded ? Number(s.score) : null,
          counted: !a.omit_from_final_grade && !s.excused, due: a.due_at, submitted: s.submitted_at, badge: badgeText, gradingType: a.grading_type,
          hypothetical: hyp !== null && (!graded || hyp !== Number(s.score)), effective, url: a.html_url, graded,
        });
      }
    }
    rows.sort((a, b) => (U.parse(a.due)?.getTime() || Infinity) - (U.parse(b.due)?.getTime() || Infinity));
    const groupStats = groups.map((g) => {
      const items = rows.filter((r) => r.groupId === g.id && r.counted && r.possible > 0 && r.effective !== null);
      const earned = items.reduce((s, r) => s + r.effective, 0);
      const possible = items.reduce((s, r) => s + r.possible, 0);
      return { ...g, graded: items.length > 0, earned, possible, pct: possible > 0 ? Math.round((earned / possible) * 100) : null, hypothetical: items.some((r) => r.hypothetical) };
    });
    const scored = groupStats.filter((g) => g.graded);
    let total = null;
    let totalDetail = '';
    if (weighted) {
      const denom = scored.reduce((s, g) => s + g.weight, 0);
      total = denom > 0 ? Math.round(scored.reduce((s, g) => s + g.weight * g.pct, 0) / denom) : null;
      totalDetail = denom > 0 ? 'weighted across scored groups' : 'no weighted group scored yet';
    } else {
      const earned = scored.reduce((s, g) => s + g.earned, 0);
      const possible = scored.reduce((s, g) => s + g.possible, 0);
      total = possible > 0 ? Math.round((earned / possible) * 100) : null;
      totalDetail = possible > 0 ? `${fmtPts(earned)} / ${fmtPts(possible)} pts · unweighted` : 'nothing graded yet';
    }
    const canvasTotal = courseInfo.score !== null && courseInfo.score !== undefined ? Math.round(Number(courseInfo.score)) : null;
    if (!whatIfOn && canvasTotal !== null) {
      total = canvasTotal;
      totalDetail = `as shown in Canvas${courseInfo.grade ? ` · ${courseInfo.grade}` : ''}`;
    }
    const GROUP_COLORS = ['#0a84ff', '#5856d6', '#ff2d55', '#ff9500', '#30b0c7', '#af52de', '#ff6b22'];
    const gray = dark ? ['#8e8e93', '#7c7c82', '#6b6b71', '#5a5a60', '#96969c'] : ['#8e8e93', '#a0a0a6', '#b0b0b6', '#78787e', '#c0c0c6'];
    const ringSrc = [{ name: 'Total', weight: null, pct: total, color: '#34c759', detail: totalDetail }]
      .concat(scored.map((g, i) => ({
        name: g.name, weight: weighted ? g.weight : null, pct: g.pct, color: GROUP_COLORS[i % GROUP_COLORS.length],
        detail: `${fmtPts(g.earned)} / ${fmtPts(g.possible)} pts${g.hypothetical ? ' · includes what-if' : ''}${g.rules?.drop_lowest ? ` · Canvas drops lowest ${g.rules.drop_lowest}` : ''}`,
      })));
    const radii = [72, 55, 38, 21, 6];
    const rings = ringSrc.slice(0, 5).map((r, i) => {
      const rad = radii[i];
      const c = 2 * Math.PI * rad;
      const pct = r.pct === null ? 0 : Math.min(r.pct, 100);
      const filled = (pct / 100) * c;
      const col = whatIfOn ? gray[i] : r.color;
      return {
        label: r.name,
        weight: r.weight === null ? (r.name === 'Total' ? (whatIfOn ? 'what-if' : 'graded only') : '—') : `${r.weight}%`,
        value: r.pct === null ? '—' : `${r.pct}%`,
        detail: r.detail, color: col, r: rad,
        track: dark ? 'rgba(255,255,255,.1)' : 'rgba(120,120,128,.16)',
        dash: `${filled.toFixed(1)} ${(c - filled).toFixed(1)}`,
        cap: pct > 0 ? 'round' : 'butt', arc: pct > 0 ? col : 'transparent',
      };
    });
    return {
      rows, total, rings, weighted,
      ungraded: groupStats.filter((g) => !g.graded).map((g) => ({ name: g.name, weight: weighted ? `${g.weight}%` : null })),
      weights: groups.map((g) => ({ name: g.name, pct: weighted ? `${g.weight}%` : '—', zero: weighted && g.weight === 0 })),
    };
  }
  function fmtPts(n) {
    return Number.isInteger(n) ? String(n) : Number(n).toFixed(1).replace(/\.0$/, '');
  }

  BCV.store = {
    env, pref, setPref, me, account, colors, courses, favorites, cards, setFavorite, currentTerm, dashboardView, setDashboardView,
    planner, classify, todo, setComplete, dismiss, invalidatePlanner, activity, activitySummary, unreadCount, groups,
    calendarContexts, selectedContexts, setSelectedContexts, calendarEvents,
    conversations, conversation, markRead, setStarred, replyTo, compose, searchRecipients, invalidateInbox,
    course, tabs, frontPage, syllabus, courseTodo, ignoreTodo, courseStream, assignments, assignment, submission, assignmentGroups, progress,
    announcements, discussions, discussion, discussionView, postEntry, markTopicRead, people, sections, courseGroups, pages, page,
    rootFolder, folderContents, folderByPath, file, quizzes, quiz, quizSubmissions, modules, gradeModel, fmtPts,
  };
})();
