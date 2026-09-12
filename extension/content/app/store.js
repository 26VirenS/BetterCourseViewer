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
  function me({ force = false, refresh = false } = {}) {
    return C.cached('me', 60 * MIN, async () => {
      try {
        const u = await C.get('/api/v1/users/self');
        return { id: String(u.id), name: u.name || u.short_name, shortName: u.short_name || u.name, avatar: u.avatar_url || null, pronouns: u.pronouns || null };
      } catch {
        const e = env();
        const cu = e.current_user || {};
        return { id: String(e.current_user_id || ''), name: cu.display_name || 'Account', shortName: cu.display_name || 'Account', avatar: cu.avatar_image_url || null, pronouns: null };
      }
    }, { force, refresh });
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
  /** The user's course colours, exactly as Canvas paints its dashboard cards. Every Canvas
   *  page already carries them (ENV.PREFERENCES.custom_colors); the API confirms them. */
  function colors({ force = false, refresh = false } = {}) {
    return C.cached('colors', 60 * MIN, async () => {
      const fromPage = env().PREFERENCES?.custom_colors || {};
      try {
        const r = await C.get('/api/v1/users/self/colors');
        return { ...fromPage, ...(r?.custom_colors || {}) };
      } catch {
        return fromPage;
      }
    }, { force, refresh });
  }

  function rawCourses({ force = false, refresh = false } = {}) {
    return C.cached('courses:all', 15 * MIN, () =>
      C.get('/api/v1/courses', {
        params: { per_page: 100, include: ['term', 'favorites', 'total_scores', 'teachers', 'sections', 'course_image'] },
        all: true,
        maxPages: 5,
      }), { force, refresh });
  }

  function cards({ force = false, refresh = false } = {}) {
    return C.cached('cards', 15 * MIN, () => C.get('/api/v1/dashboard/dashboard_cards'), { force, refresh });
  }

  const now = () => new Date();
  const isoDay = (d) => U.startOfDay(d).toISOString().replace(/\.\d{3}Z$/, 'Z');
  // Canvas answers these when one of the contexts in a request is off-limits
  const REFUSED = new Set([401, 403, 404]);
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
  async function courses({ force = false, refresh = false } = {}) {
    const [list, cols, dark] = await Promise.all([rawCourses({ force, refresh }), colors({ force, refresh }), Promise.resolve(BCV.early?.isDark?.() ?? false)]);
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
  async function favorites({ force = false, refresh = false } = {}) {
    const [all, cardList] = await Promise.all([courses({ force, refresh }), cards({ force, refresh }).catch(() => [])]);
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
    if (on) {
      // Until you star something, Canvas shows every current course on the dashboard.
      // Starring the first one would leave it alone up there, so star what is shown too.
      const all = await courses().catch(() => []);
      if (all.length && !all.some((c) => c.favorite)) {
        const shown = await cards().catch(() => []);
        for (const k of shown || []) if (String(k.id) !== String(courseId)) await C.post(`/api/v1/users/self/favorites/courses/${k.id}`, {}).catch(() => {});
      }
      await C.post(`/api/v1/users/self/favorites/courses/${courseId}`, {});
    } else await C.del(`/api/v1/users/self/favorites/courses/${courseId}`);
    await Promise.all([C.invalidate('courses:all'), C.invalidate('cards')]);
  }

  /** A course nickname, Canvas's own: every course list then carries it as the name (the real
   *  name stays as original_name). An empty name removes it. */
  async function setNickname(courseId, name) {
    const nick = String(name || '').trim();
    if (nick) await C.put(`/api/v1/users/self/course_nicknames/${courseId}`, { nickname: nick });
    else await C.del(`/api/v1/users/self/course_nicknames/${courseId}`);
    await Promise.all([C.invalidate('courses:all'), C.invalidate('cards'), C.invalidate(`course:${courseId}`)]);
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
  function rawPlanner(days = 21, { force = false, refresh = false } = {}) {
    return C.cached(`planner:${days}`, 3 * MIN, () =>
      C.get('/api/v1/planner/items', { params: { start_date: isoDays(-7), end_date: isoDays(days), per_page: 100 }, all: true, maxPages: 5 }), { force, refresh });
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
    else if (type === 'planner_note') { kind = 'My task'; icon = IC.task; isDue = false; } // a task the student added (a Canvas planner note)
    else if (type === 'announcement') { kind = 'Announcement'; icon = IC.bell; isDue = false; }
    else if (type === 'assessment_request') { kind = 'Peer review'; icon = IC.people; }
    else if (type === 'assignment') { isDue = !!(p.due_at || item.plannable_date); }
    const subs = item.submissions && typeof item.submissions === 'object' ? item.submissions : {};
    const date = U.parse(p.due_at || p.todo_date || p.start_at || item.plannable_date);
    const courseId = item.course_id ? String(item.course_id) : null;
    const course = courseId && courseMap ? courseMap.get(courseId) : null;
    const custom = type === 'planner_note';
    return {
      id: `${type}:${item.plannable_id}`, // stable: what priority and other student metadata key on
      raw: item,
      type,
      kind,
      icon,
      isDue,
      date,
      custom, // not a course: excluded from every course count, and the only rows that can be deleted
      title: p.title || p.name || 'Untitled',
      points: p.points_possible ?? null,
      courseId,
      courseName: course?.name || item.context_name || (custom ? 'My task' : ''),
      course,
      submitted: !!(subs.submitted || subs.graded),
      graded: !!subs.graded,
      missing: !!subs.missing,
      late: !!subs.late,
      excused: !!subs.excused,
      complete: !!item.planner_override?.marked_complete,
      dismissed: !!item.planner_override?.dismissed,
      url: item.html_url || (custom ? '/#todo' : courseId ? `/courses/${courseId}` : '/'),
    };
  }

  /** A task of the student's own: a Canvas planner note (mockup 12), so it lives in the same
   *  planner list as everything else and on every device. `todoDate` is an ISO datetime or null
   *  (a task the student left undated stays undated — nothing invents a due date). */
  async function createNote({ title, todoDate = null, courseId = null }) {
    const body = { title, todo_date: todoDate, course_id: courseId || undefined };
    const note = await C.post('/api/v1/planner_notes', body);
    await invalidatePlanner();
    return note;
  }
  async function deleteNote(id) {
    await C.del(`/api/v1/planner_notes/${encodeURIComponent(id)}`);
    await invalidatePlanner();
  }

  async function planner({ force = false, refresh = false, days = 21 } = {}) {
    const [items, cs] = await Promise.all([rawPlanner(days, { force, refresh }), courses().catch(() => [])]);
    const map = new Map(cs.map((c) => [c.id, c]));
    return (items || []).map((it) => classify(it, map)).filter((it) => it.date);
  }

  /** Everything in the To Do window (today through the next 7 days), including
   *  items already completed, submitted or dismissed; the screen decides what to show. */
  async function todoWindow(opts = {}) {
    const items = await planner(opts);
    const t = now();
    const end = U.addDays(U.startOfDay(t), 8);
    return items.filter((it) => it.date >= U.startOfDay(t) && it.date < end && it.type !== 'announcement');
  }
  /** Items still open on the To Do list: not done, not dismissed. */
  async function todo(opts = {}) {
    return (await todoWindow(opts)).filter((it) => !it.complete && !it.dismissed && !it.submitted);
  }

  async function override(item, patch) {
    const ov = item.raw.planner_override;
    const result = ov?.id
      ? await C.put(`/api/v1/planner/overrides/${ov.id}`, patch)
      : await C.post('/api/v1/planner/overrides', { plannable_type: item.type, plannable_id: item.raw.plannable_id, ...patch });
    item.raw.planner_override = result && result.id ? result : { ...(ov || {}), ...patch };
    item.complete = !!item.raw.planner_override.marked_complete;
    item.dismissed = !!item.raw.planner_override.dismissed;
    await invalidatePlanner();
    return result;
  }
  const setComplete = (item, complete) => override(item, { marked_complete: complete });
  const dismiss = (item) => override(item, { dismissed: true });
  const restore = (item) => override(item, { dismissed: false, marked_complete: false });
  async function invalidatePlanner() {
    await Promise.all([C.invalidate('planner:21'), C.invalidate('planner:14'), C.invalidate('planner:60')]);
  }

  // ---- activity + counts ------------------------------------------------------------------
  function activity({ force = false, refresh = false } = {}) {
    return C.cached('activity', 3 * MIN, () => C.get('/api/v1/users/self/activity_stream', { params: { per_page: 40, only_active_courses: true } }), { force, refresh });
  }
  function activitySummary({ force = false, refresh = false } = {}) {
    return C.cached('activity:summary', 3 * MIN, () => C.get('/api/v1/users/self/activity_stream/summary', { params: { only_active_courses: true } }), { force, refresh });
  }
  /** Announcements across the current courses (the Announcements API), newest
   *  first, each with its read state. Canvas refuses the whole request when one
   *  course is off-limits, so a refused chunk is retried one course at a time. */
  /** Canvas's own recently-visited list (what the History tray shows). */
  function history({ force = false, refresh = false } = {}) {
    return C.cached('history', 2 * MIN, () => C.get('/api/v1/users/self/history', { params: { per_page: 40 } }), { force, refresh });
  }
  /** The account's help links (the Help tray reads the same list). */
  function helpLinks({ force = false, refresh = false } = {}) {
    return C.cached('helplinks', 60 * MIN, () => C.get('/help_links'), { force, refresh });
  }
  function announcementsFeed({ force = false, refresh = false } = {}) {
    return C.cached('annfeed', 3 * MIN, async () => {
      const cs = (await courses()).filter((c) => c.state === 'current');
      if (!cs.length) return [];
      const params = { start_date: isoDay(U.addDays(now(), -60)), end_date: isoDay(U.addDays(now(), 2)), active_only: true, per_page: 50 };
      const fetchCodes = (codes) => C.get('/api/v1/announcements', { params: { ...params, 'context_codes[]': codes }, all: true, maxPages: 3 });
      const out = [];
      for (let i = 0; i < cs.length; i += 10) {
        const codes = cs.slice(i, i + 10).map((c) => `course_${c.id}`);
        try {
          out.push(...((await fetchCodes(codes)) || []));
        } catch (err) {
          if (!REFUSED.has(err.status)) throw err;
          for (const code of codes) {
            try {
              out.push(...((await fetchCodes([code])) || []));
            } catch (err2) {
              if (!REFUSED.has(err2.status)) throw err2;
            }
          }
        }
      }
      return out.sort((a, b) => (U.parse(b.posted_at) || 0) - (U.parse(a.posted_at) || 0));
    }, { force, refresh });
  }
  // ---- notifications: what needs attention, from what Canvas already reports ----------------
  const NOTIF_STATE = 'notifState'; // { read: { id: true }, gone: { id: true } }, local: Canvas cannot mark these
  const stripHtml = (html) => String(html || '').replace(/<[^>]+>/g, ' ').replace(/&nbsp;/g, ' ').replace(/\s+/g, ' ').trim();
  const localDay = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  const pastText = (date, t) => {
    const hrs = Math.max(1, Math.round((t - date) / 3600e3));
    return hrs < 24 ? `${U.plural(hrs, 'hour')} past` : `${U.plural(Math.round(hrs / 24), 'day')} past`;
  };
  async function notifState() {
    const st = (await pref(NOTIF_STATE)) || {};
    return { read: st.read && typeof st.read === 'object' ? st.read : {}, gone: st.gone && typeof st.gone === 'object' ? st.gone : {} };
  }
  async function setNotifState(next) {
    await setPref(NOTIF_STATE, { read: next.read || {}, gone: next.gone || {} });
  }
  /** Overdue and Due soon from the planner (due items with nothing submitted: the last 14 days, the
   *  next 48 hours); Graded and Feedback from Submission items in the activity stream (a score, a
   *  comment); Announcements from the unread ones; System from Canvas's notification messages and
   *  the local grade snapshot. Each carries the URL Canvas gave it. */
  async function notifications({ force = false, refresh = false } = {}) {
    const t = now();
    const [items, stream, anns, cs, snaps] = await Promise.all([
      planner({ force, refresh }).catch(() => []),
      activity({ force, refresh }).catch(() => []),
      announcementsFeed({ force, refresh }).catch(() => []),
      courses().catch(() => []),
      pref('gpaSnapshots').catch(() => null),
    ]);
    const byId = new Map(cs.map((c) => [c.id, c]));
    const courseOf = (id) => (id === null || id === undefined ? null : byId.get(String(id))) || null;
    const H = 3600e3;
    const out = [];
    for (const it of items || []) {
      if (!it.isDue || it.submitted || it.complete || it.dismissed || it.excused || !it.date) continue;
      const ms = it.date - t;
      const pts = it.points !== null && it.points !== undefined ? ` · ${it.points} pts` : '';
      const base = { title: it.title, courseId: it.courseId, course: it.course?.shortName || it.courseName || '', color: it.course?.color || null, when: it.date, whenText: `Due ${U.fmtAt(it.date)}`, url: it.url, action: it.type === 'quiz' ? 'Start quiz' : 'Submit now' };
      if (ms < 0 && ms > -14 * 24 * H) out.push({ ...base, id: `planner:${it.id}`, cat: 'overdue', note: `${it.missing ? 'Marked missing' : 'Nothing submitted'} · ${pastText(it.date, t)}${pts}` });
      else if (ms >= 0 && ms < 48 * H) out.push({ ...base, id: `planner:${it.id}`, cat: 'soon', note: `${it.kind}${pts} · ${it.type === 'quiz' ? 'not started' : 'no submission'}` });
    }
    for (const a of stream || []) {
      const when = U.parse(a.updated_at || a.created_at);
      const c = courseOf(a.course_id);
      const base = { courseId: c?.id || (a.course_id ? String(a.course_id) : null), course: c?.shortName || a.context_name || '', color: c?.color || null, when, url: a.html_url || (a.course_id ? `/courses/${a.course_id}` : '/') };
      if (a.type === 'Submission') {
        const scored = (a.score !== undefined && a.score !== null) || (a.grade !== undefined && a.grade !== null && a.grade !== '');
        const comments = Array.isArray(a.submission_comments) ? a.submission_comments.filter((x) => x && x.comment) : [];
        const name = a.assignment?.name || String(a.title || 'Submission').replace(/\s+graded\b.*$/i, '');
        const possible = a.assignment?.points_possible;
        if (scored) out.push({ ...base, id: `stream:${a.id}`, cat: 'graded', title: `${name} graded`, note: `${a.score ?? a.grade}${possible !== undefined && possible !== null ? ` / ${possible}` : ''}${a.grade && a.score !== undefined && a.score !== null && String(a.grade) !== String(a.score) ? ` · ${a.grade}` : ''}`, action: 'See grades' });
        if (comments.length) {
          const last = comments[comments.length - 1];
          out.push({ ...base, id: `stream:${a.id}:comment`, cat: 'feedback', when: U.parse(last.created_at) || when, title: `${last.author_name || 'Your instructor'} left a comment on ${name}`, note: `“${stripHtml(last.comment).slice(0, 140)}”`, action: 'Read comment' });
        }
      } else if (a.type === 'Message') {
        out.push({ ...base, id: `stream:${a.id}`, cat: 'system', title: a.title || a.notification_category || 'Notification', note: stripHtml(a.message).slice(0, 140) || a.notification_category || '', action: 'Open' });
      }
    }
    for (const an of anns || []) {
      if (an.read_state && an.read_state !== 'unread') continue;
      const cid = String(an.context_code || '').replace(/^course_/, '') || (an.course_id ? String(an.course_id) : '');
      const c = courseOf(cid);
      out.push({ id: `ann:${an.id}`, cat: 'announce', title: an.title || 'Announcement', courseId: c?.id || cid || null, course: c?.shortName || c?.name || an.context_name || '', color: c?.color || null, when: U.parse(an.posted_at || an.created_at), note: an.author?.display_name ? `From ${an.author.display_name}` : 'Announcement', action: 'Read', url: an.html_url || (cid ? `/courses/${cid}/announcements/${an.id}` : '/') });
    }
    if (Array.isArray(snaps) && snaps.length) {
      const last = snaps[snaps.length - 1];
      if (last?.date === localDay(t)) out.push({ id: `local:snapshot:${last.date}`, cat: 'system', title: 'Grade snapshot recorded', courseId: null, course: 'Simpl Courses', color: '#8e8e93', when: U.startOfDay(t), whenText: 'Today', note: `${U.plural(snaps.length, 'day')} of history stored locally`, action: 'See trend', url: '/grades' });
    }
    const dir = (n) => (n.cat === 'soon' ? 1 : -1); // due soon: soonest first; everything else: newest first
    return out.sort((a, b) => (a.cat === b.cat ? dir(a) * ((a.when?.getTime() || 0) - (b.when?.getTime() || 0)) : 0));
  }
  /** The badge on the sidebar: alerts neither read nor dismissed. */
  async function notifUnread(opts = {}) {
    const [feed, st] = await Promise.all([notifications(opts), notifState()]);
    return feed.filter((n) => !st.gone[n.id] && !st.read[n.id]).length;
  }

  /** Stream items opened from here. Canvas has no API to mark a stream item
   *  read, so the blue dots are also cleared locally once an item is opened. */
  async function streamSeen() {
    return new Set(((await pref('seenStream')) || []).map(String));
  }
  async function markStreamSeen(id) {
    const list = ((await pref('seenStream')) || []).map(String).filter((x) => x !== String(id));
    list.push(String(id));
    await setPref('seenStream', list.slice(-400));
  }
  /** A course colour, saved to the user's Canvas colours (the same ones Canvas uses). */
  async function setColor(courseId, hex) {
    await C.put(`/api/v1/users/self/colors/course_${courseId}`, { hexcode: hex });
    await C.invalidate('colors');
  }
  function unreadCount({ force = false, refresh = false } = {}) {
    return C.cached('unread', 2 * MIN, async () => {
      try {
        const r = await C.get('/api/v1/conversations/unread_count');
        return Number(r?.unread_count) || 0;
      } catch {
        return 0;
      }
    }, { force, refresh });
  }

  // ---- groups ---------------------------------------------------------------------------------
  function groups({ force = false, refresh = false } = {}) {
    return C.cached('groups', 15 * MIN, () => C.get('/api/v1/users/self/groups', { params: { per_page: 50, include: ['group_category'] }, all: true }), { force, refresh });
  }

  // ---- calendar -------------------------------------------------------------------------------
  async function calendarContexts() {
    const [u, cs, gs] = await Promise.all([me(), courses(), groups().catch(() => [])]);
    const list = [{ code: `user_${u.id}`, name: u.name, color: '#0a84ff', kind: 'user' }];
    for (const c of cs) if (c.state !== 'past') list.push({ code: `course_${c.id}`, name: c.name, color: c.color, kind: 'course', courseId: c.id });
    for (const g of gs || []) list.push({ code: `group_${g.id}`, name: g.name, color: '#6b5f7a', kind: 'group' });
    return list;
  }
  /** Which calendars to show: our own saved choice, else the selection the
   *  user made in Canvas's calendar, else the first ten. An empty or stale
   *  list falls back too, so the calendar is never blank for lack of a pick. */
  async function selectedContexts(all) {
    const e = env();
    const codes = new Set(all.map((c) => c.code));
    const stored = await pref('calendarContexts');
    const fromCanvas = Array.isArray(e.SELECTED_CONTEXT_CODES) ? e.SELECTED_CONTEXT_CODES : null;
    for (const cand of [stored, fromCanvas]) {
      if (!Array.isArray(cand) || !cand.length) continue;
      const kept = cand.filter((c) => codes.has(c));
      if (kept.length) return kept;
    }
    return all.slice(0, 10).map((c) => c.code);
  }
  async function setSelectedContexts(codes) {
    await setPref('calendarContexts', codes);
  }
  /** Events for a date range and a set of context codes. Canvas takes at most
   *  ten codes per request and refuses the whole request when one of them is
   *  off-limits (a concluded or restricted course), so a chunk that fails is
   *  retried one context at a time and the refused codes are reported back
   *  rather than blanking the calendar. Returns { events, refused }. */
  function calendarEvents(start, end, codes, { force = false, refresh = false } = {}) {
    const s = isoDay(start);
    const e = isoDay(U.addDays(U.startOfDay(end), 1));
    const key = `cal:${s.slice(0, 10)}:${e.slice(0, 10)}:${codes.join(',')}`;
    return C.cached(key, 5 * MIN, async () => {
      const events = [];
      const refused = [];
      const seen = new Set();
      const add = (list) => {
        for (const ev of list || []) {
          const k = String(ev.id);
          if (seen.has(k)) continue;
          seen.add(k);
          events.push(ev);
        }
      };
      const fetchCodes = async (chunk) => {
        const params = { start_date: s, end_date: e, 'context_codes[]': chunk, per_page: 100 };
        const [ev, as] = await Promise.all([
          C.get('/api/v1/calendar_events', { params: { ...params, type: 'event' }, all: true, maxPages: 5 }),
          C.get('/api/v1/calendar_events', { params: { ...params, type: 'assignment' }, all: true, maxPages: 5 }),
        ]);
        return [...(ev || []), ...(as || [])];
      };
      const chunks = [];
      for (let i = 0; i < codes.length; i += 10) chunks.push(codes.slice(i, i + 10));
      await Promise.all(chunks.map(async (chunk) => {
        try {
          add(await fetchCodes(chunk));
        } catch (err) {
          if (!REFUSED.has(err.status)) throw err;
          if (chunk.length === 1) {
            refused.push(chunk[0]);
            return;
          }
          for (const code of chunk) {
            try {
              add(await fetchCodes([code]));
            } catch (err2) {
              if (!REFUSED.has(err2.status)) throw err2;
              refused.push(code);
            }
          }
        }
      }));
      return { events, refused };
    }, { force, refresh });
  }
  /** Planner items for any range: the calendar's stand-in when Canvas will
   *  not answer calendar_events at all (the dashboard reads the same API). */
  function plannerRange(start, end, { force = false, refresh = false } = {}) {
    const s = isoDay(start);
    const e = isoDay(U.addDays(U.startOfDay(end), 1));
    return C.cached(`planner:range:${s.slice(0, 10)}:${e.slice(0, 10)}`, 5 * MIN, async () => {
      const [items, cs] = await Promise.all([
        C.get('/api/v1/planner/items', { params: { start_date: s, end_date: e, per_page: 100 }, all: true, maxPages: 8 }),
        courses().catch(() => []),
      ]);
      const map = new Map(cs.map((c) => [c.id, c]));
      return (items || []).map((it) => classify(it, map)).filter((it) => it.date);
    }, { force, refresh });
  }

  // ---- inbox ----------------------------------------------------------------------------------
  function conversations({ scope = 'inbox', filter = null, force = false, refresh = false } = {}) {
    const params = { per_page: 50 };
    if (scope && scope !== 'inbox') params.scope = scope;
    if (filter) params['filter[]'] = filter;
    return C.cached(`conv:${scope}:${filter || ''}`, 2 * MIN, async () => {
      const r = await C.get('/api/v1/conversations', { params });
      return Array.isArray(r) ? r : (r?.conversations || []);
    }, { force, refresh });
  }
  function conversation(id, { force = true, refresh = false } = {}) {
    return C.cached(`conv:${id}`, MIN, () => C.get(`/api/v1/conversations/${id}`), { force, refresh });
  }
  async function invalidateInbox() {
    await C.invalidatePrefix('conv:');
    await C.invalidate('unread');
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
  async function course(id, { force = false, refresh = false } = {}) {
    const list = await courses({ force, refresh });
    const found = list.find((c) => c.id === String(id));
    if (found) return found;
    const c = await C.cached(`course:${id}`, 15 * MIN, () => C.get(`/api/v1/courses/${id}`, { params: { include: ['term', 'teachers', 'total_scores', 'sections', 'course_image'] } }), { force, refresh });
    const cols = await colors();
    const color = cols[`course_${id}`] || U.FALLBACK_COLORS[0];
    const enr = (c.enrollments || [])[0] || {};
    return {
      id: String(c.id), raw: c, name: c.name, originalName: c.original_name || c.name, nickname: c.original_name ? c.name : null, code: c.course_code, term: c.term?.name || '', favorite: !!c.is_favorite,
      state: courseState(c), role: roleLabel(enr.type), score: enr.computed_current_score ?? null, grade: enr.computed_current_grade ?? null, teachers: (c.teachers || []).map((t) => t.display_name),
      sections: (c.sections || []).map((s) => s.name), image: c.image_download_url || null, defaultView: c.default_view || 'wiki', weighted: !!c.apply_assignment_group_weights,
      color, palette: U.palette(color, BCV.early?.isDark?.() ?? false), url: `/courses/${id}`,
    };
  }
  // `kind` is 'courses' or 'groups': the same endpoints exist under both.
  function tabs(id, { force = false, refresh = false, kind = 'courses' } = {}) {
    return C.cached(`tabs:${kind}:${id}`, 30 * MIN, () => C.get(`/api/v1/${kind}/${id}/tabs`), { force, refresh });
  }
  function frontPage(id, { force = false, refresh = false, kind = 'courses' } = {}) {
    return C.cached(`front:${kind}:${id}`, 10 * MIN, () => C.get(`/api/v1/${kind}/${id}/front_page`).catch(() => null), { force, refresh });
  }
  /** One group, shaped like a course so the course tab builders can draw it. */
  async function group(id, { force = false, refresh = false } = {}) {
    const g = await C.cached(`group:${id}`, 15 * MIN, () => C.get(`/api/v1/groups/${id}`, { params: { include: ['group_category', 'users'] } }), { force, refresh });
    const cs = await courses().catch(() => []);
    const course = g.course_id ? cs.find((c) => c.id === String(g.course_id)) : null;
    const color = course?.color || '#5856d6';
    return {
      id: String(g.id), raw: g, name: g.name, originalName: g.name, nickname: null, code: g.name, term: course?.term || (g.context_type === 'Account' ? 'Account group' : ''), favorite: false,
      state: course?.state || 'current', role: 'Member', score: null, grade: null, teachers: [], sections: [], image: g.avatar_url || null, defaultView: 'feed', weighted: false,
      color, palette: U.palette(color, BCV.early?.isDark?.() ?? false), url: `/groups/${id}`, kind: 'groups', course, membersCount: g.members_count ?? null, description: g.description || '',
    };
  }
  function syllabus(id, { force = false, refresh = false } = {}) {
    return C.cached(`syllabus:${id}`, 10 * MIN, () => C.get(`/api/v1/courses/${id}`, { params: { include: ['syllabus_body'] } }).then((c) => c?.syllabus_body || ''), { force, refresh });
  }
  function courseTodo(id, { force = false, refresh = false } = {}) {
    return C.cached(`ctodo:${id}`, 3 * MIN, () => C.get(`/api/v1/courses/${id}/todo`, { params: { per_page: 20 } }).catch(() => []), { force, refresh });
  }
  async function ignoreTodo(item, courseId) {
    const url = item.ignore || item.ignore_permanently;
    if (!url) return;
    const path = url.replace(/^https?:\/\/[^/]+/, '');
    await C.del(path);
    await C.invalidate(`ctodo:${courseId}`);
  }
  function courseStream(id, { force = false, refresh = false, kind = 'courses' } = {}) {
    return C.cached(`cstream:${kind}:${id}`, 3 * MIN, () => C.get(`/api/v1/${kind}/${id}/activity_stream`, { params: { per_page: 40 } }), { force, refresh });
  }
  function assignments(id, { force = false, refresh = false } = {}) {
    return C.cached(`assignments:${id}`, 10 * MIN, () =>
      C.get(`/api/v1/courses/${id}/assignments`, { params: { per_page: 100, include: ['submission', 'all_dates'], order_by: 'due_at' }, all: true, maxPages: 4 }), { force, refresh });
  }
  function assignment(id, aid, { force = false, refresh = false } = {}) {
    return C.cached(`assignment:${id}:${aid}`, 5 * MIN, () =>
      C.get(`/api/v1/courses/${id}/assignments/${aid}`, { params: { include: ['submission', 'score_statistics', 'can_submit'] } }), { force, refresh });
  }
  function submission(id, aid, { force = false, refresh = false } = {}) {
    return C.cached(`submission:${id}:${aid}`, 5 * MIN, () =>
      // submission_history carries per-question points for quiz attempts (the feedback screen reads it)
      C.get(`/api/v1/courses/${id}/assignments/${aid}/submissions/self`, { params: { include: ['submission_comments', 'rubric_assessment', 'submission_history'] } }).catch(() => null), { force, refresh });
  }
  function assignmentGroups(id, { force = false, refresh = false } = {}) {
    return C.cached(`agroups:${id}`, 10 * MIN, () =>
      C.get(`/api/v1/courses/${id}/assignment_groups`, { params: { per_page: 50, include: ['assignments', 'submission'], exclude_assignment_submission_types: ['wiki_page'] }, all: true, maxPages: 3 }), { force, refresh });
  }
  // ---- handing work in ------------------------------------------------------------------------
  /** Tools the instructor enabled for handing work in (Box, Office 365, …): Canvas draws one
   *  tab per tool with a homework_submission placement; the submit screen draws one row. */
  function homeworkTools(id, { force = false, refresh = false } = {}) {
    return C.cached(`hwtools:${id}`, 30 * MIN, () =>
      C.get(`/api/v1/courses/${id}/external_tools`, { params: { placement: 'homework_submission', include_parents: true, per_page: 50 }, all: true, maxPages: 2 }), { force, refresh });
  }
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  const attachmentId = (o) => o?.id ?? o?.attachment?.id ?? o?.attachment_id ?? null;
  const progressFileId = (p) => p?.results?.id ?? p?.results?.attachment_id ?? p?.results?.attachment?.id ?? null;
  const preflightPath = (cid, aid) => `/api/v1/courses/${cid}/assignments/${aid}/submissions/self/files`;

  /** Canvas's three-step upload for a submission file: preflight for the storage URL,
   *  POST the bytes there (progress reaches the row), then confirm when storage
   *  answers with a location instead of the file. Returns the file id. */
  async function uploadSubmissionFile(cid, aid, file, onProgress) {
    const pre = await C.post(preflightPath(cid, aid), { name: file.name, size: file.size, content_type: file.type || undefined, on_duplicate: 'rename' });
    if (!pre?.upload_url) throw new Error('Canvas did not return an upload URL');
    const form = new FormData();
    for (const [k, v] of Object.entries(pre.upload_params || {})) form.append(k, v);
    form.append(pre.file_param || 'file', file, file.name); // the file must be the last field
    let done = await C.upload(pre.upload_url, form, { onProgress });
    // inst-fs (and S3 without a redirect) answer with the confirm URL; Canvas's own storage with the file
    if (done?.location) done = await C.get(done.location);
    const id = attachmentId(done);
    if (!id) throw new Error('the upload did not finish');
    return String(id);
  }

  /** A file a tool handed back (Box, Office 365, …): Canvas fetches it from the tool's URL
   *  itself and reports through a Progress; poll until the file exists. Returns the file id. */
  async function uploadSubmissionFileFromUrl(cid, aid, { url, name, contentType }) {
    const pre = await C.post(preflightPath(cid, aid), { url, name: name || undefined, content_type: contentType || undefined, on_duplicate: 'rename' });
    const deadline = Date.now() + 90e3;
    if (pre?.progress?.id) {
      let p = pre.progress;
      while (p.workflow_state !== 'completed') {
        if (p.workflow_state === 'failed') throw new Error(p.message || 'Canvas could not fetch the file from the tool');
        if (Date.now() > deadline) throw new Error('Canvas is still fetching the file from the tool — try again in a moment');
        await sleep(1000);
        p = await C.get(`/api/v1/progress/${p.id}`);
      }
      const id = progressFileId(p) || attachmentId(pre);
      if (!id) throw new Error('Canvas did not say which file it saved');
      return String(id);
    }
    if (pre?.status_url && pre.upload_status) { // older Canvas: the file exists already, poll until it is ready
      let s = pre;
      while (s.upload_status === 'pending') {
        if (Date.now() > deadline) throw new Error('Canvas is still fetching the file from the tool — try again in a moment');
        await sleep(1000);
        s = await C.get(pre.status_url);
      }
      if (s.upload_status === 'errored') throw new Error(s.message || 'Canvas could not fetch the file from the tool');
      return String(attachmentId(s) || pre.id);
    }
    const id = attachmentId(pre);
    if (!id) throw new Error('Canvas did not accept the file');
    return String(id);
  }

  /** Hand the work in: one POST with exactly what Canvas's own form sends. */
  async function submitAssignment(cid, aid, { type, fileIds = [], body = '', url = '', comment = '' }) {
    const submission = { submission_type: type };
    if (type === 'online_upload') submission.file_ids = fileIds;
    else if (type === 'online_text_entry') submission.body = body;
    else submission.url = url; // online_url and basic_lti_launch
    const payload = { submission };
    if (comment) payload.comment = { text_comment: comment };
    const result = await C.post(`/api/v1/courses/${cid}/assignments/${aid}/submissions`, payload);
    await invalidateAssignment(cid, aid);
    return result;
  }
  async function invalidateAssignment(cid, aid) {
    await Promise.all([C.invalidate(`assignment:${cid}:${aid}`), C.invalidate(`submission:${cid}:${aid}`), C.invalidate(`assignments:${cid}`), C.invalidate(`agroups:${cid}`), C.invalidate(`ctodo:${cid}`), invalidatePlanner()]);
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
  function announcements(id, { force = false, refresh = false, kind = 'courses' } = {}) {
    return C.cached(`ann:${kind}:${id}`, 5 * MIN, () =>
      C.get(`/api/v1/${kind}/${id}/discussion_topics`, { params: { only_announcements: true, per_page: 50 }, all: true, maxPages: 2 }), { force, refresh });
  }
  function discussions(id, { force = false, refresh = false, kind = 'courses' } = {}) {
    return C.cached(`disc:${kind}:${id}`, 5 * MIN, () =>
      C.get(`/api/v1/${kind}/${id}/discussion_topics`, { params: { per_page: 50, order_by: 'recent_activity', include: ['all_dates'] }, all: true, maxPages: 3 }), { force, refresh });
  }
  function discussion(id, tid, { force = false, refresh = false, kind = 'courses' } = {}) {
    return C.cached(`disc:${kind}:${id}:${tid}`, 3 * MIN, () => C.get(`/api/v1/${kind}/${id}/discussion_topics/${tid}`, { params: { include: ['all_dates'] } }), { force, refresh });
  }
  function discussionView(id, tid, { force = true, refresh = false, kind = 'courses' } = {}) {
    return C.cached(`discview:${kind}:${id}:${tid}`, MIN, () => C.get(`/api/v1/${kind}/${id}/discussion_topics/${tid}/view`).catch(() => null), { force, refresh });
  }
  async function postEntry(id, tid, message, parentId = null, { kind = 'courses' } = {}) {
    const r = parentId
      ? await C.post(`/api/v1/${kind}/${id}/discussion_topics/${tid}/entries/${parentId}/replies`, { message })
      : await C.post(`/api/v1/${kind}/${id}/discussion_topics/${tid}/entries`, { message });
    await Promise.all([C.invalidate(`discview:${kind}:${id}:${tid}`), C.invalidate(`disc:${kind}:${id}`), C.invalidate(`disc:${kind}:${id}:${tid}`)]);
    return r;
  }
  async function markTopicRead(id, tid, { kind = 'courses' } = {}) {
    try {
      await C.put(`/api/v1/${kind}/${id}/discussion_topics/${tid}/read_all`, {});
      await Promise.all([C.invalidate(`disc:${kind}:${id}`), C.invalidate(`ann:${kind}:${id}`), C.invalidate('annfeed'), C.invalidate('activity'), C.invalidate('activity:summary'), C.invalidate(`cstream:${kind}:${id}`)]);
    } catch {
      /* ignore */
    }
  }
  function people(id, { force = false, refresh = false, kind = 'courses' } = {}) {
    return C.cached(`people:${kind}:${id}`, 15 * MIN, () =>
      C.get(`/api/v1/${kind}/${id}/users`, { params: { per_page: 100, include: kind === 'groups' ? ['avatar_url'] : ['enrollments', 'avatar_url', 'pronouns'], sort: 'username' }, all: true, maxPages: 5 }), { force, refresh });
  }
  function sections(id, { force = false, refresh = false } = {}) {
    return C.cached(`sections:${id}`, 30 * MIN, () => C.get(`/api/v1/courses/${id}/sections`, { params: { per_page: 100 } }).catch(() => []), { force, refresh });
  }
  function courseGroups(id, { force = false, refresh = false } = {}) {
    return C.cached(`cgroups:${id}`, 15 * MIN, () => C.get(`/api/v1/courses/${id}/groups`, { params: { per_page: 100, include: ['group_category'] } }).catch(() => []), { force, refresh });
  }
  function pages(id, { force = false, refresh = false, kind = 'courses' } = {}) {
    return C.cached(`pages:${kind}:${id}`, 10 * MIN, () =>
      C.get(`/api/v1/${kind}/${id}/pages`, { params: { per_page: 100, sort: 'title', published: true }, all: true, maxPages: 3 }), { force, refresh });
  }
  function page(id, slug, { force = false, refresh = false, kind = 'courses' } = {}) {
    return C.cached(`page:${kind}:${id}:${slug}`, 10 * MIN, () => C.get(`/api/v1/${kind}/${id}/pages/${encodeURIComponent(slug)}`), { force, refresh });
  }
  function rootFolder(id, { force = false, refresh = false, kind = 'courses' } = {}) {
    return C.cached(`folder:root:${kind}:${id}`, 15 * MIN, () => C.get(`/api/v1/${kind}/${id}/folders/root`), { force, refresh });
  }
  function folderContents(folderId, { force = false, refresh = false } = {}) {
    return C.cached(`folder:${folderId}`, 10 * MIN, async () => {
      const [folders, files] = await Promise.all([
        C.get(`/api/v1/folders/${folderId}/folders`, { params: { per_page: 100 }, all: true, maxPages: 2 }).catch(() => []),
        C.get(`/api/v1/folders/${folderId}/files`, { params: { per_page: 100, include: ['user'] }, all: true, maxPages: 3 }).catch(() => []),
      ]);
      return { folders: folders || [], files: files || [] };
    }, { force, refresh });
  }
  function folderByPath(id, path, { force = false, refresh = false, kind = 'courses' } = {}) {
    return C.cached(`folder:path:${kind}:${id}:${path}`, 10 * MIN, () =>
      C.get(`/api/v1/${kind}/${id}/folders/by_path/${path.split('/').map(encodeURIComponent).join('/')}`), { force, refresh });
  }
  function file(fileId, { force = false, refresh = false } = {}) {
    return C.cached(`file:${fileId}`, 10 * MIN, () => C.get(`/api/v1/files/${fileId}`), { force, refresh });
  }
  function quizzes(id, { force = false, refresh = false } = {}) {
    return C.cached(`quizzes:${id}`, 10 * MIN, () => C.get(`/api/v1/courses/${id}/quizzes`, { params: { per_page: 100 }, all: true, maxPages: 3 }), { force, refresh });
  }
  function quiz(id, qid, { force = false, refresh = false } = {}) {
    return C.cached(`quiz:${id}:${qid}`, 5 * MIN, () => C.get(`/api/v1/courses/${id}/quizzes/${qid}`), { force, refresh });
  }
  function quizSubmissions(id, qid, { force = false, refresh = false } = {}) {
    return C.cached(`quizsubs:${id}:${qid}`, 3 * MIN, () => C.get(`/api/v1/courses/${id}/quizzes/${qid}/submissions`).then((r) => r?.quiz_submissions || []).catch(() => []), { force, refresh });
  }
  /** Quiz attempt API: everything the quiz screen needs, uncached. */
  /** Attempts used and allowed for a quiz, from its submissions: Canvas returns the latest
   *  submission whose `attempt` counts all so far (an open one counts once it is finished);
   *  `extra_attempts` on it are ones the instructor granted. allowed null = unlimited. */
  function quizAttemptLimit(q, subs) {
    const list = subs || [];
    const used = list.reduce((m, s) => Math.max(m, s.workflow_state === 'untaken' ? Math.max(0, (Number(s.attempt) || 1) - 1) : (Number(s.attempt) || 0)), 0);
    const extra = list.reduce((m, s) => Math.max(m, Number(s.extra_attempts) || 0), 0);
    const allowed = q && Number(q.allowed_attempts) > 0 ? Number(q.allowed_attempts) + extra : null;
    return { used, allowed, left: allowed === null ? null : Math.max(0, allowed - used), open: list.some((s) => s.workflow_state === 'untaken') };
  }

  const quizApi = {
    /** Start an attempt, or return the open one. */
    async start(courseId, quizId, accessCode) {
      const existing = await C.get(`/api/v1/courses/${courseId}/quizzes/${quizId}/submissions`).then((r) => r?.quiz_submissions || []).catch(() => []);
      const open = existing.find((s) => s.workflow_state === 'untaken');
      if (open) return open;
      // Never open an attempt past the quiz's limit: Canvas returns one submission per user whose
      // `attempt` is the count so far, plus any extra attempts the instructor granted.
      const q = await quiz(courseId, quizId).catch(() => null);
      const limit = quizAttemptLimit(q, existing);
      if (limit.allowed !== null && limit.used >= limit.allowed) throw new Error(`No attempts left — this quiz allows ${U.plural(limit.allowed, 'attempt')}.`);
      const body = {};
      if (accessCode) body.access_code = accessCode;
      const r = await C.post(`/api/v1/courses/${courseId}/quizzes/${quizId}/submissions`, body);
      const sub = (r?.quiz_submissions || [])[0];
      if (!sub) throw new Error('Canvas did not start an attempt.');
      await C.invalidate(`quizsubs:${courseId}:${quizId}`);
      return sub;
    },
    /** The attempt's questions, merged with their full text and answers. */
    async questions(sub) {
      const r = await C.get(`/api/v1/quiz_submissions/${sub.id}/questions`, { params: { include: ['quiz_question'] } });
      const full = new Map((r?.quiz_questions || []).map((q) => [String(q.id), q]));
      return (r?.quiz_submission_questions || []).map((q) => ({ ...(full.get(String(q.id)) || {}), ...q, id: String(q.id) }))
        .sort((a, b) => (a.position || 0) - (b.position || 0));
    },
    answer(sub, questionId, answer) {
      return C.post(`/api/v1/quiz_submissions/${sub.id}/questions`, { attempt: sub.attempt, validation_token: sub.validation_token, quiz_questions: [{ id: questionId, answer }] });
    },
    flag(sub, questionId, on) {
      return C.put(`/api/v1/quiz_submissions/${sub.id}/questions/${questionId}/${on ? 'flag' : 'unflag'}`, { attempt: sub.attempt, validation_token: sub.validation_token });
    },
    time(courseId, quizId, sub) {
      return C.get(`/api/v1/courses/${courseId}/quizzes/${quizId}/submissions/${sub.id}/time`);
    },
    async complete(courseId, quizId, sub, accessCode) {
      const body = { attempt: sub.attempt, validation_token: sub.validation_token };
      if (accessCode) body.access_code = accessCode;
      const r = await C.post(`/api/v1/courses/${courseId}/quizzes/${quizId}/submissions/${sub.id}/complete`, body);
      await Promise.all([C.invalidate(`quizsubs:${courseId}:${quizId}`), C.invalidate(`quiz:${courseId}:${quizId}`), invalidatePlanner()]);
      return (r?.quiz_submissions || [])[0] || r;
    },
  };

  function modules(id, { force = false, refresh = false } = {}) {
    return C.cached(`modules:${id}`, 10 * MIN, () =>
      C.get(`/api/v1/courses/${id}/modules`, { params: { per_page: 50, include: ['items', 'content_details'] }, all: true, maxPages: 4 }), { force, refresh });
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
    // Groups that carry weight come first (in Canvas's order), then the 0% ones.
    const ordered = [...groups].sort((a, b) => (weighted ? Number(b.weight > 0) - Number(a.weight > 0) : 0) || (a.position || 0) - (b.position || 0));
    const GROUP_COLORS = ['#0a84ff', '#5856d6', '#ff2d55', '#ff9500', '#30b0c7', '#af52de', '#ff6b22', '#c8901c'];
    const groupStats = ordered.map((g, i) => {
      const items = rows.filter((r) => r.groupId === g.id && r.counted && r.possible > 0 && r.effective !== null);
      const omitted = rows.filter((r) => r.groupId === g.id && !r.counted).map((r) => r.name);
      const earned = items.reduce((s, r) => s + r.effective, 0);
      const possible = items.reduce((s, r) => s + r.possible, 0);
      return {
        ...g, color: GROUP_COLORS[i % GROUP_COLORS.length], graded: items.length > 0, earned, possible, omitted,
        pct: possible > 0 ? Math.round((earned / possible) * 100) : null, hypothetical: items.some((r) => r.hypothetical), zero: weighted && g.weight === 0,
      };
    });
    const scored = groupStats.filter((g) => g.graded);
    let total = null;
    let note = '';
    if (weighted) {
      const denom = scored.reduce((s, g) => s + g.weight, 0);
      total = denom > 0 ? Math.round(scored.reduce((s, g) => s + g.weight * g.pct, 0) / denom) : null;
      note = denom > 0 ? 'Weighted across the groups that have graded work.' : 'No weighted group has graded work yet.';
    } else {
      const earned = scored.reduce((s, g) => s + g.earned, 0);
      const possible = scored.reduce((s, g) => s + g.possible, 0);
      total = possible > 0 ? Math.round((earned / possible) * 100) : null;
      note = possible > 0 ? `${fmtPts(earned)} / ${fmtPts(possible)} pts, graded work only.` : 'Nothing graded yet.';
    }
    if (whatIfOn) note = `What-if ${note.charAt(0).toLowerCase()}${note.slice(1)}`;
    const canvasTotal = courseInfo.score !== null && courseInfo.score !== undefined ? Math.round(Number(courseInfo.score)) : null;
    if (!whatIfOn && canvasTotal !== null) {
      total = canvasTotal;
      note = `As shown in Canvas${courseInfo.grade ? ` · ${courseInfo.grade}` : ''}. ${weighted ? 'Weighted across the groups that have graded work.' : 'Graded work only.'}`;
    }
    const gray = dark ? ['#8e8e93', '#7c7c82', '#6b6b71', '#5a5a60', '#96969c'] : ['#8e8e93', '#a0a0a6', '#b0b0b6', '#78787e', '#c0c0c6'];
    const colorOf = (i, color) => (whatIfOn ? gray[Math.min(i, gray.length - 1)] : color);
    // rings: outer = total, then one per group with graded work (five at most; the rest are legend only)
    const radii = [72, 56, 40, 24, 10];
    const widths = [12, 12, 12, 11, 7];
    const ringSrc = [{ pct: total, color: '#34c759', zero: false }].concat(scored.map((g) => ({ pct: g.pct, color: g.color, zero: g.zero })));
    const rings = ringSrc.slice(0, radii.length).map((r, i) => {
      const rad = radii[i];
      const c = 2 * Math.PI * rad;
      const pct = r.pct === null ? 0 : Math.min(r.pct, 100);
      const filled = (pct / 100) * c;
      const col = colorOf(i, r.color);
      const patternId = `bcv-stipple-${courseInfo.id}-${i}`;
      return {
        r: rad, w: widths[i], color: col, zero: r.zero, patternId,
        track: dark ? 'rgba(255,255,255,.1)' : 'rgba(120,120,128,.16)',
        dash: `${filled.toFixed(1)} ${(c - filled).toFixed(1)}`,
        cap: pct > 0 ? 'round' : 'butt', arc: pct > 0 ? (r.zero ? `url(#${patternId})` : col) : 'transparent',
      };
    });
    const weightText = (g) => (weighted ? (g.weight === 0 ? 'not weighted' : `${g.weight}% of grade`) : 'by points');
    const legend = scored.map((g, i) => {
      const ringed = i + 1 < radii.length;
      const bits = [`${fmtPts(g.earned)} / ${fmtPts(g.possible)} pts`];
      if (g.hypothetical) bits.push('includes what-if');
      if (g.omitted.length === 1) bits.push(`${g.omitted[0]} excluded`);
      else if (g.omitted.length > 1) bits.push(`${g.omitted.length} not counted`);
      if (g.rules?.drop_lowest) bits.push(`Canvas drops lowest ${g.rules.drop_lowest}`);
      if (!ringed) bits.push('legend only');
      return { id: g.id, label: g.name, detail: bits.join(' · '), weightText: weightText(g), value: g.pct === null ? '—' : `${g.pct}%`, pct: g.pct, weight: g.weight, color: colorOf(i + 1, g.color), ringed, zero: g.zero };
    });
    const ungraded = groupStats.filter((g) => !g.graded).map((g) => ({ name: g.name, weightText: weighted ? `${g.weight}%` : '' }));
    const bearing = groupStats.filter((g) => g.weight > 0);
    const weightBar = weighted ? bearing.map((g) => ({ name: g.name, weight: g.weight, graded: g.graded, color: g.graded ? (whatIfOn ? gray[0] : g.color) : null, scoreLabel: g.graded ? `${g.pct}%` : 'ungraded' })) : [];
    const zeros = weighted ? groupStats.filter((g) => g.weight === 0) : [];
    const listNames = (arr) => (arr.length <= 1 ? arr.join('') : `${arr.slice(0, -1).join(', ')} and ${arr[arr.length - 1]}`);
    const one = zeros.length === 1;
    const weightNote = zeros.length
      ? `${listNames(zeros.map((g) => g.name))} ${one ? 'carries' : 'carry'} 0% weight — ${zeros.some((g) => g.graded) ? (one ? 'it shows as a ring but never moves' : 'they show as rings but never move') : (one ? 'it never moves' : 'they never move')} the total.`
      : '';
    return {
      rows, total, weighted, rings, legend, ungraded, weightBar, weightNote,
      stipples: rings.filter((r) => r.zero).map((r) => ({ id: r.patternId, color: r.color })),
      center: { label: whatIfOn ? 'What-if total' : 'Total', value: total === null ? '—' : `${total}%`, color: whatIfOn ? gray[0] : '#34c759', note },
      weightSum: bearing.reduce((s, g) => s + g.weight, 0),
      weights: groups.map((g) => ({ name: g.name, pct: weighted ? `${g.weight}%` : '—', zero: weighted && g.weight === 0 })),
    };
  }
  function fmtPts(n) {
    return Number.isInteger(n) ? String(n) : Number(n).toFixed(1).replace(/\.0$/, '');
  }

  BCV.store = {
    env, pref, setPref, me, account, colors, courses, favorites, cards, setFavorite, setNickname, currentTerm, dashboardView, setDashboardView,
    planner, classify, todo, todoWindow, setComplete, dismiss, restore, invalidatePlanner, createNote, deleteNote, activity, activitySummary, unreadCount, groups, group,
    announcementsFeed, streamSeen, markStreamSeen, setColor, history, helpLinks,
    calendarContexts, selectedContexts, setSelectedContexts, calendarEvents, plannerRange,
    conversations, conversation, markRead, setStarred, replyTo, compose, searchRecipients, invalidateInbox,
    course, tabs, frontPage, syllabus, courseTodo, ignoreTodo, courseStream, assignments, assignment, submission, assignmentGroups, progress,
    announcements, discussions, discussion, discussionView, postEntry, markTopicRead, people, sections, courseGroups, pages, page,
    rootFolder, folderContents, folderByPath, file, quizzes, quiz, quizSubmissions, quizApi, modules, gradeModel, fmtPts,
    notifications, notifState, setNotifState, notifUnread,
    homeworkTools, uploadSubmissionFile, uploadSubmissionFileFromUrl, submitAssignment, invalidateAssignment, quizAttemptLimit,
  };
})();
