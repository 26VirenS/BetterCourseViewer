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
  // ---- the student's own metadata for this site (priorities, views, goals…) --------------------
  // One object in storage.local, shared by every tab on the site. A write reads the object again
  // first and changes only its own key, and a change made in another tab replaces this tab's copy
  // — so a tab that has sat open for a while never puts a stale object back over what was set in
  // another (the priorities on To Do were the usual casualty).
  // Within a tab the writes go one at a time, each read-then-write against storage, and a write
  // shows in this tab's copy the moment it is asked for (a read right after it sees it), so two
  // quick writes never lose each other's key either.
  const prefKey = `prefs:${location.host}`;
  let prefsCache = null;
  const pending = []; // writes asked for but not in storage yet: laid over whatever storage says
  const readPrefs = async () => {
    try {
      const r = await api.storage.local.get(prefKey);
      return r[prefKey] && typeof r[prefKey] === 'object' ? { ...r[prefKey] } : {};
    } catch {
      return {};
    }
  };
  const applyOp = (p, op) => {
    if ('value' in op) p[op.key] = op.value;
    else {
      const cur = p[op.key] && typeof p[op.key] === 'object' ? { ...p[op.key] } : {};
      for (const [k, v] of Object.entries(op.patch)) { if (v === null || v === undefined) delete cur[k]; else cur[k] = v; }
      p[op.key] = cur;
    }
    return p;
  };
  const withPending = (p) => pending.reduce((acc, op) => applyOp(acc, op), { ...p });
  async function prefs() {
    if (!prefsCache) prefsCache = withPending(await readPrefs());
    return prefsCache;
  }
  async function pref(key, fallback = null) {
    const p = await prefs();
    return p[key] === undefined ? fallback : p[key];
  }
  let writing = Promise.resolve();
  function write(op) {
    pending.push(op);
    if (prefsCache) prefsCache = applyOp({ ...prefsCache }, op);
    const done = writing.then(async () => {
      const p = applyOp(await readPrefs(), op); // the latest object in storage, this change on it
      try {
        await api.storage.local.set({ [prefKey]: p });
      } catch {
        /* ignore */
      }
      pending.splice(pending.indexOf(op), 1);
      prefsCache = withPending(p);
      return p[op.key];
    });
    writing = done.catch(() => {});
    return done;
  }
  /** Writes one key. */
  const setPref = (key, value) => write({ key, value });
  /** Changes some entries of an object-valued pref (a null entry deletes), and resolves to the
   *  whole object as it is now — for maps that several tabs edit, such as the To Do priorities. */
  const mergePref = (key, patch) => write({ key, patch: patch || {} });
  try {
    api.storage.onChanged.addListener((changes, area) => {
      if (area !== 'local' || !changes[prefKey]) return;
      const next = changes[prefKey].newValue;
      prefsCache = withPending(next && typeof next === 'object' ? next : {});
    });
  } catch {
    /* no change events here: each write still reads the latest object first */
  }

  // ---- user + site --------------------------------------------------------------
  function me({ force = false, refresh = false } = {}) {
    return C.cached('me', 60 * MIN, async () => {
      try {
        // the user record, and the profile beside it: the e-mail and the login are only on the profile
        const [u, p] = await Promise.all([C.get('/api/v1/users/self'), C.get('/api/v1/users/self/profile').catch(() => null)]);
        return { id: String(u.id), name: u.name || u.short_name, shortName: u.short_name || u.name, avatar: u.avatar_url || p?.avatar_url || null, pronouns: u.pronouns || p?.pronouns || null, email: p?.primary_email || u.email || null, loginId: p?.login_id || u.login_id || null };
      } catch {
        const e = env();
        const cu = e.current_user || {};
        return { id: String(e.current_user_id || ''), name: cu.display_name || 'Account', shortName: cu.display_name || 'Account', avatar: cu.avatar_image_url || null, pronouns: null, email: null, loginId: null };
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

  function rawCourses({ force = false, refresh = false, maxAge = 0 } = {}) {
    return C.cached('courses:all', 15 * MIN, () =>
      C.get('/api/v1/courses', {
        params: { per_page: 100, include: ['term', 'favorites', 'total_scores', 'teachers', 'sections', 'course_image'] },
        all: true,
        maxPages: 5,
      }), { force, refresh, maxAge });
  }

  function cards({ force = false, refresh = false } = {}) {
    return C.cached('cards', 5 * MIN, () => C.get('/api/v1/dashboard/dashboard_cards'), { force, refresh });
  }

  const now = () => new Date();
  const isoDay = (d) => U.startOfDay(d).toISOString().replace(/\.\d{3}Z$/, 'Z');
  // Canvas answers these when one of the contexts in a request is off-limits
  const REFUSED = new Set([401, 403, 404]);
  function courseState(c, t = now()) {
    const term = c.term || {};
    const enr = (c.enrollments || [])[0] || {};
    // a course's own dates govern only when it says so (restrict_enrollments_to_course_dates); the
    // term's otherwise — and a term that has ended is a past course, whatever the course's own say
    const own = c.restrict_enrollments_to_course_dates === true;
    const start = U.parse((own && c.start_at) || term.start_at);
    const end = U.parse((own && c.end_at) || term.end_at);
    if (c.workflow_state === 'completed' || enr.enrollment_state === 'completed' || (end && end < t)) return 'past';
    if (enr.enrollment_state === 'invited' || enr.enrollment_state === 'creation_pending' || (start && start > t)) return 'future';
    return 'current';
  }

  /** All courses, decorated with colour, palette, favourite flag and state. Decorated once per
   *  answer: the same list, colours and mode give the same objects back (every screen asks for
   *  the courses, some several times a draw, and each ask used to build the whole set anew). */
  let coursesMemo = null; // { list, cols, dark, out }
  async function courses({ force = false, refresh = false, maxAge = 0 } = {}) {
    const [list, cols, dark] = await Promise.all([rawCourses({ force, refresh, maxAge }), colors({ force, refresh }), Promise.resolve(BCV.early?.isDark?.() ?? false)]);
    if (coursesMemo && coursesMemo.list === list && coursesMemo.cols === cols && coursesMemo.dark === dark) return coursesMemo.out;
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
        termStart: c.term?.start_at || null, // (the term's dates, for the course header)
        termEnd: c.term?.end_at || null,
        favorite: !!c.is_favorite,
        state: courseState(c),
        role: roleLabel(enr.type || enr.role),
        score: enr.computed_current_score ?? null,
        grade: enr.computed_current_grade ?? null,
        finalScore: enr.computed_final_score ?? null, // (ungraded work counted as zero: what the term would end on today)
        finalGrade: enr.computed_final_grade ?? null,
        hideFinal: !!c.hide_final_grades, // (the teacher withholds the total: Canvas sends no current score then)
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
    coursesMemo = { list, cols, dark, out };
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
    } else {
      // with nothing starred, Canvas shows every current course and none is a favourite to
      // delete: taking one off the dashboard means starring all the others, so the rest stay
      const all = await courses().catch(() => []);
      if (all.length && !all.some((c) => c.favorite)) {
        const shown = await cards().catch(() => []);
        for (const k of shown || []) if (String(k.id) !== String(courseId)) await C.post(`/api/v1/users/self/favorites/courses/${k.id}`, {}).catch(() => {});
      } else await C.del(`/api/v1/users/self/favorites/courses/${courseId}`);
    }
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
    return C.cached(`planner:${days}`, 1.5 * MIN, () => // (what is due changes under a tab left open: short-lived)
      C.get('/api/v1/planner/items', { params: { start_date: isoDays(-14), end_date: isoDays(days), per_page: 100 }, all: true, maxPages: 5 }), { force, refresh }); // (two weeks back: what is overdue, on To Do and in Notifications alike)
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
      feedback: !!subs.has_feedback, // (the teacher wrote back: a comment, a rubric)
      newActivity: !!item.new_activity, // (something happened since you last looked, as Canvas's own list marks it)
      redo: !!subs.redo_request,
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

  /** The student's own planner items — tasks added here or in Canvas's planner without a course,
   *  and personal events — over a long span (three months back, a year on): a task of your own
   *  is not bound to the seven-day window, so one dated last week or next month is still read.
   *  Asked for under the user's own context code, so the answer is a handful of items. */
  async function rawUserPlanner({ force = false, refresh = false } = {}) {
    const u = await me();
    return C.cached('planner:mine', 3 * MIN, () =>
      C.get('/api/v1/planner/items', { params: { start_date: isoDays(-90), end_date: isoDays(365), 'context_codes[]': [`user_${u.id}`], per_page: 100 }, all: true, maxPages: 3 }), { force, refresh });
  }
  /** Everything on the To Do list, including items already completed, submitted or dismissed
   *  (the screen decides what to show): course work from today through the next 7 days, and every
   *  task of the student's own whatever its date — a task is theirs until they tick it off or
   *  delete it, so one from yesterday or one for next month stays on the list. */
  async function todoWindow(opts = {}) {
    const [items, mineRaw] = await Promise.all([planner(opts), rawUserPlanner(opts).catch(() => [])]);
    const t = now();
    const start = U.startOfDay(t);
    const end = U.addDays(start, 8);
    const inWindow = (it) => it.date >= start && it.date < end;
    // past due with nothing handed in stays on the list until it is done or dismissed, as Canvas's
    // own To Do keeps it (the planner is read from a week back for it)
    const overdueOpen = (it) => it.isDue && it.date < start && !it.submitted && !it.complete && !it.dismissed && !it.excused;
    const seen = new Set(items.map((it) => it.id));
    const mine = (mineRaw || []).map((it) => classify(it)).filter((it) => it.date && it.custom && !seen.has(it.id));
    return [...items.filter((it) => it.type !== 'announcement' && (it.custom || inWindow(it) || overdueOpen(it))), ...mine].sort((a, b) => a.date - b.date);
  }
  /** Items still open on the To Do list: not done, not dismissed, not handed in, not excused. */
  async function todo(opts = {}) {
    return (await todoWindow(opts)).filter((it) => !it.complete && !it.dismissed && !it.submitted && !it.excused);
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
    await C.invalidatePrefix('planner:'); // (the windows, the student's own tasks, the overrides, and the calendar's ranges)
  }
  /** Every planner override of the student's (marked complete, dismissed), whatever the item's date:
   *  what the Overdue card reads for late work, which comes from the course's assignments rather
   *  than the planner window and so carries no override of its own. */
  async function plannerOverrides({ force = false } = {}) {
    return C.cached('planner:overrides', 3 * MIN, () => C.get('/api/v1/planner/overrides', { params: { per_page: 100 }, all: true, maxPages: 5 }), { force });
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
      // every ten-course chunk at once, and on a refusal every course of that chunk at once — the
      // same shape as the calendar: one round trip rather than one per chunk or per course
      const chunks = [];
      for (let i = 0; i < cs.length; i += 10) chunks.push(cs.slice(i, i + 10).map((c) => `course_${c.id}`));
      await Promise.all(chunks.map(async (codes) => {
        try {
          out.push(...((await fetchCodes(codes)) || []));
        } catch (err) {
          if (!REFUSED.has(err.status)) throw err;
          await Promise.all(codes.map(async (code) => {
            try {
              out.push(...((await fetchCodes([code])) || []));
            } catch (err2) {
              if (!REFUSED.has(err2.status)) throw err2;
            }
          }));
        }
      }));
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
  /** Saves the read and dismissed marks — only for alerts still in the feed when `liveIds` is
   *  given, so the maps do not grow for the life of the account. */
  async function setNotifState(next, liveIds = null) {
    const keep = (m) => (liveIds ? Object.fromEntries(Object.entries(m || {}).filter(([id]) => liveIds.has(id))) : (m || {}));
    await setPref(NOTIF_STATE, { read: keep(next.read), gone: keep(next.gone) });
  }
  /** Overdue and Due soon from the planner (due items with nothing submitted: the last 14 days, the
   *  next 48 hours); Graded and Feedback from Submission items in the activity stream (a score, a
   *  comment); Messages and Discussions from its unread Conversation and DiscussionTopic items (a
   *  new message, new replies); Announcements from the unread ones; System from Canvas's
   *  notification messages and the local grade snapshot. Each carries the URL Canvas gave it. */
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
    const label = (c) => c?.shortName || c?.name || ''; // (the course list carries no nickname field: its name is the label)
    const H = 3600e3;
    const out = [];
    for (const it of items || []) {
      if (!it.isDue || it.submitted || it.complete || it.dismissed || it.excused || !it.date) continue;
      const ms = it.date - t;
      const pts = it.points !== null && it.points !== undefined ? ` · ${it.points} pts` : '';
      const base = { title: it.title, courseId: it.courseId, course: label(it.course) || it.courseName || '', color: it.course?.color || null, when: it.date, whenText: `Due ${U.fmtAt(it.date)}`, url: it.url, action: it.type === 'quiz' ? 'Start quiz' : 'Submit now' };
      if (ms < 0 && ms > -14 * 24 * H) out.push({ ...base, id: `planner:${it.id}`, cat: 'overdue', note: `${it.missing ? 'Marked missing' : 'Nothing submitted'} · ${pastText(it.date, t)}${pts}` });
      else if (ms >= 0 && ms < 48 * H) out.push({ ...base, id: `planner:${it.id}`, cat: 'soon', note: `${it.kind}${pts} · ${it.type === 'quiz' ? 'not started' : 'no submission'}` });
    }
    for (const a of stream || []) {
      const when = U.parse(a.updated_at || a.created_at);
      const c = courseOf(a.course_id);
      const base = { courseId: c?.id || (a.course_id ? String(a.course_id) : null), course: label(c) || a.context_name || '', color: c?.color || null, when, url: a.html_url || (a.course_id ? `/courses/${a.course_id}` : '/') };
      if (a.type === 'Submission') {
        // a score the teacher has not posted yet is not a grade to announce (posted_at null; absent means posted)
        const posted = a.posted_at === undefined || a.posted_at !== null;
        const scored = posted && ((a.score !== undefined && a.score !== null) || (a.grade !== undefined && a.grade !== null && a.grade !== ''));
        const comments = Array.isArray(a.submission_comments) ? a.submission_comments.filter((x) => x && x.comment) : [];
        const name = a.assignment?.name || String(a.title || 'Submission').replace(/\s+graded\b.*$/i, '');
        const possible = a.assignment?.points_possible;
        if (scored) out.push({ ...base, id: `stream:${a.id}`, cat: 'graded', title: `${name} graded`, note: `${a.score ?? a.grade}${possible !== undefined && possible !== null ? ` / ${possible}` : ''}${a.grade && a.score !== undefined && a.score !== null && String(a.grade) !== String(a.score) ? ` · ${a.grade}` : ''}`, action: 'See grades' });
        if (comments.length) {
          const last = comments[comments.length - 1];
          out.push({ ...base, id: `stream:${a.id}:comment`, cat: 'feedback', when: U.parse(last.created_at) || when, title: `${last.author_name || 'Your instructor'} left a comment on ${name}`, note: `“${stripHtml(last.comment).slice(0, 140)}”`, action: 'Read comment' });
        }
      } else if (a.type === 'Message') {
        // Canvas's own notification: its category is always said (Due Date, Grading, Course Content…), then its words
        out.push({ ...base, id: `stream:${a.id}`, cat: 'system', title: a.title || a.notification_category || 'Notification', note: [a.notification_category, stripHtml(a.message).slice(0, 140)].filter(Boolean).join(' · '), action: 'Open' });
      } else if (a.type === 'Conversation' && a.read_state === false) {
        // a message in the Inbox not read yet: the subject, and how it starts
        const preview = stripHtml(a.message).slice(0, 140);
        out.push({ ...base, id: `stream:${a.id}`, cat: 'message', title: a.title || 'New message', note: preview ? `“${preview}”` : `${U.plural(a.participant_count || 2, 'person')} in the conversation`, action: 'Read', url: a.conversation_id ? `/conversations?id=${a.conversation_id}` : (a.html_url || '/conversations') });
      } else if (a.type === 'DiscussionTopic' && a.read_state === false) {
        // a discussion with something new in it: how many replies it has grown to
        const n = a.total_root_discussion_entries;
        out.push({ ...base, id: `stream:${a.id}`, cat: 'discuss', title: a.title || 'Discussion', note: [n !== undefined && n !== null ? U.plural(n, 'reply', 'replies') : '', stripHtml(a.message).slice(0, 100)].filter(Boolean).join(' · '), action: 'Open' });
      }
    }
    for (const an of anns || []) {
      if (an.read_state && an.read_state !== 'unread') continue;
      const cid = String(an.context_code || '').replace(/^course_/, '') || (an.course_id ? String(an.course_id) : '');
      const c = courseOf(cid);
      out.push({ id: `ann:${an.id}`, cat: 'announce', title: an.title || 'Announcement', courseId: c?.id || cid || null, course: label(c) || an.context_name || '', color: c?.color || null, when: U.parse(an.posted_at || an.created_at), note: an.author?.display_name ? `From ${an.author.display_name}` : 'Announcement', action: 'Read', url: an.html_url || (cid ? `/courses/${cid}/announcements/${an.id}` : '/') });
    }
    if (Array.isArray(snaps) && snaps.length) {
      const last = snaps[snaps.length - 1];
      if (last?.date === localDay(t)) out.push({ id: `local:snapshot:${last.date}`, cat: 'system', title: 'Grade snapshot recorded', courseId: null, course: 'Simpl Courses', color: '#8e8e93', when: U.startOfDay(t), whenText: 'Today', note: `${U.plural(snaps.length, 'day')} of history stored locally`, action: 'See trend', url: '/grades' });
    }
    // by kind, then within a kind: due soon soonest first, everything else newest first (a comparator
    // that returned 0 across kinds was no order at all, and the engine was free to shuffle a kind's items)
    const RANK = { overdue: 0, soon: 1, graded: 2, feedback: 3, message: 4, discuss: 5, announce: 6, system: 7 };
    const dir = (n) => (n.cat === 'soon' ? 1 : -1);
    return out.sort((a, b) => ((RANK[a.cat] ?? 9) - (RANK[b.cat] ?? 9)) || dir(a) * ((a.when?.getTime() || 0) - (b.when?.getTime() || 0)));
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
    // the biggest pages Canvas gives: a student's groups run back through every course they took, and each page is a round trip of its own
    return C.cached('groups', 15 * MIN, () => C.get('/api/v1/users/self/groups', { params: { per_page: 100, include: ['group_category'] }, all: true, maxPages: 3 }), { force, refresh });
  }

  // ---- calendar -------------------------------------------------------------------------------
  /** Calendars Canvas has already refused on this page (a concluded course, a restricted section).
   *  Asking for them again only costs a round trip and gets the same answer. */
  const knownRefused = new Set();
  async function calendarContexts() {
    const [u, cs, gs] = await Promise.all([me(), courses(), groups().catch(() => [])]);
    const list = [{ code: `user_${u.id}`, name: u.name, color: '#0a84ff', kind: 'user' }];
    for (const c of cs) if (c.state !== 'past') list.push({ code: `course_${c.id}`, name: c.name, color: c.color, kind: 'course', courseId: c.id, favorite: !!c.favorite });
    for (const g of gs || []) list.push({ code: `group_${g.id}`, name: g.name, color: '#6b5f7a', kind: 'group' });
    return list;
  }
  /** The calendars that are the user's own: the favourite courses — the one course list every
   *  screen follows — or, until one is starred, every current course. Everything else (the
   *  personal calendar, courses not starred, groups) is an "other" calendar. */
  function ownContexts(all) {
    const courses = all.filter((c) => c.kind === 'course');
    const favs = courses.filter((c) => c.favorite);
    return favs.length ? favs : courses;
  }
  /** Which calendars to show: our own saved choice, else the user's own calendars (at most
   *  Canvas's ten). A stale saved list falls back too, so the calendar is never blank for lack
   *  of a pick. The other calendars are off until turned on. */
  async function selectedContexts(all) {
    const codes = new Set(all.map((c) => c.code));
    const stored = await pref('calendarContexts');
    if (Array.isArray(stored) && stored.length) {
      const kept = stored.filter((c) => codes.has(c));
      if (kept.length) return kept;
    }
    return ownContexts(all).slice(0, 10).map((c) => c.code);
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
      if (force || refresh) knownRefused.clear(); // asked afresh: give every calendar another go
      const events = [];
      // a calendar already known to be off-limits is not asked again: the first month pays for
      // finding out, the rest of the session does not
      const refused = codes.filter((c) => knownRefused.has(c));
      const live = codes.filter((c) => !knownRefused.has(c));
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
      for (let i = 0; i < live.length; i += 10) chunks.push(live.slice(i, i + 10));
      await Promise.all(chunks.map(async (chunk) => {
        try {
          add(await fetchCodes(chunk));
        } catch (err) {
          if (!REFUSED.has(err.status)) throw err;
          if (chunk.length === 1) {
            refused.push(chunk[0]);
            knownRefused.add(chunk[0]);
            return;
          }
          // One calendar in the chunk is off-limits and Canvas refuses the lot, so each is asked
          // for on its own — all at once, not one after another: this is the difference between
          // one round trip and ten of them on a slow connection.
          await Promise.all(chunk.map(async (code) => {
            try {
              add(await fetchCodes([code]));
            } catch (err2) {
              if (!REFUSED.has(err2.status)) throw err2;
              refused.push(code);
              knownRefused.add(code);
            }
          }));
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

  // ---- appointments (Canvas's Scheduler) ------------------------------------------------------
  /** The appointment groups the student can sign up for — office hours, conferences, any set of
   *  times a teacher opened — each with its times, how many places each has left, and the times
   *  this student already holds (child_events and reserved_times). Kept only briefly: a place can
   *  be taken by someone else at any moment, so the sheet asks again each time it opens. */
  function appointmentGroups({ force = false, refresh = false } = {}) {
    return C.cached('appointments', 2 * MIN, () =>
      C.get('/api/v1/appointment_groups', { params: { scope: 'reservable', include: ['appointments', 'child_events', 'participant_count', 'reserved_times'], include_past_appointments: false, per_page: 50 }, all: true, maxPages: 3 }), { force, refresh });
  }
  /** Reserve a time (a calendar event of an appointment group). Canvas answers with the
   *  reservation: an event of the student's own, on the course's calendar. */
  async function reserveAppointment(slotId, { comments = '' } = {}) {
    const r = await C.post(`/api/v1/calendar_events/${slotId}/reservations`, comments ? { comments } : {});
    await invalidateAppointments();
    return r;
  }
  /** Give a time back: the reservation is the student's own event, deleted. */
  async function cancelReservation(reservationId, { reason = '' } = {}) {
    const r = await C.del(`/api/v1/calendar_events/${reservationId}`, reason ? { params: { cancel_reason: reason } } : undefined);
    await invalidateAppointments();
    return r;
  }
  /** After a reservation either way: the groups and every month read so far (the reservation is
   *  an event on the calendar) are asked for afresh. */
  async function invalidateAppointments() {
    await Promise.all([C.invalidate('appointments'), C.invalidatePrefix('cal:')]);
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
  /** One group, shaped like a course so the course tab builders can draw it.
   *
   *  The group's own call is the only one waited on. Its course is worth showing — the colour, the
   *  term and a link back — but /api/v1/courses is the slowest call the app makes, and a group that
   *  waited for it would sit blank for as long as the course list takes. So the course is folded in
   *  if the list is already here, and otherwise arrives on `withCourse`, which the screen can take
   *  up after it has drawn. */
  async function group(id, { force = false, refresh = false } = {}) {
    // (the members count comes with the group; the members themselves are the People tab's, not carried on every open)
    const g = await C.cached(`group:${id}`, 15 * MIN, () => C.get(`/api/v1/groups/${id}`, { params: { include: ['group_category'] } }), { force, refresh });
    const shape = (course) => ({
      id: String(g.id), raw: g, name: g.name, originalName: g.name, nickname: null, code: g.name, term: course?.term || (g.context_type === 'Account' ? 'Account group' : ''), favorite: false,
      state: course?.state || 'current', role: 'Member', score: null, grade: null, teachers: [], sections: [], image: g.avatar_url || null, defaultView: 'feed', weighted: false,
      color: course?.color || '#5856d6', palette: U.palette(course?.color || '#5856d6', BCV.early?.isDark?.() ?? false),
      url: `/groups/${id}`, kind: 'groups', course, membersCount: g.members_count ?? null, description: g.description || '',
    });
    const find = (cs) => (g.course_id ? (cs || []).find((c) => c.id === String(g.course_id)) || null : null);
    // both of the calls courses() is built from, or it would still wait on the one that is missing
    const here = C.ready('courses:all') && C.ready('colors') ? await courses().catch(() => []) : null;
    const out = shape(find(here));
    // a group with no course of its own is already whole; so is one drawn from a list we had
    out.withCourse = here || !g.course_id ? Promise.resolve(null) : courses().catch(() => []).then((cs) => find(cs));
    return out;
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
  function assignments(id, { force = false, refresh = false, maxAge = 0 } = {}) {
    return C.cached(`assignments:${id}`, 3 * MIN, () =>
      C.get(`/api/v1/courses/${id}/assignments`, { params: { per_page: 100, include: ['submission', 'all_dates'], order_by: 'due_at' }, all: true, maxPages: 4 }), { force, refresh, maxAge });
  }
  function assignment(id, aid, { force = false, refresh = false, maxAge = 0 } = {}) {
    return C.cached(`assignment:${id}:${aid}`, 5 * MIN, () =>
      C.get(`/api/v1/courses/${id}/assignments/${aid}`, { params: { include: ['submission', 'score_statistics', 'can_submit'] } }), { force, refresh, maxAge });
  }
  function submission(id, aid, { force = false, refresh = false } = {}) {
    return C.cached(`submission:${id}:${aid}`, 5 * MIN, () =>
      // submission_history carries per-question points for quiz attempts (the feedback screen reads it)
      C.get(`/api/v1/courses/${id}/assignments/${aid}/submissions/self`, { params: { include: ['submission_comments', 'rubric_assessment', 'submission_history'] } }).catch(() => null), { force, refresh });
  }
  function assignmentGroups(id, { force = false, refresh = false, maxAge = 0 } = {}) {
    return C.cached(`agroups:${id}`, 10 * MIN, () =>
      C.get(`/api/v1/courses/${id}/assignment_groups`, { params: { per_page: 50, include: ['assignments', 'submission', 'score_statistics'], exclude_assignment_submission_types: ['wiki_page'] }, all: true, maxPages: 3 }), { force, refresh, maxAge }); // (score_statistics: the class's mean, high and low on each marked assignment)
  }
  // ---- grades change behind the page's back ------------------------------------------------------
  // A grade lands in Canvas while this page keeps what it read: a tool (an LTI plugin marking work
  // in its own frame), a teacher elsewhere. Every screen that shows a score forgets its answers here,
  // so the next draw asks again — on the way back from a tool's page or frame, and on returning to
  // the tab after a while away (app.js). And a grades screen never draws from an answer older than
  // `freshness.grades`, whatever the memo's TTL (a test winds it down to 0).
  const freshness = { grades: 30e3 };
  async function invalidateGrades() {
    await Promise.all([
      C.invalidate('courses:all'), C.invalidate('cards'), C.invalidatePrefix('course:'),
      C.invalidatePrefix('assignments:'), C.invalidatePrefix('agroups:'), C.invalidatePrefix('assignment:'), C.invalidatePrefix('submission:'),
      C.invalidate('activity'), C.invalidate('activity:summary'), invalidatePlanner(),
    ]);
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
  /** A comment on your own submission — the same thread the instructor's feedback comes back on.
   *  Canvas files a comment against an attempt, so it is named: unpinned, feedback on a first draft
   *  and on the final hand-in end up in the same thread. */
  async function commentOnSubmission(cid, aid, text, attempt = null) {
    const body = String(text || '').trim();
    if (!body) throw new Error('Nothing to send.');
    const comment = { text_comment: body };
    if (attempt) comment.attempt = attempt;
    const result = await C.put(`/api/v1/courses/${cid}/assignments/${aid}/submissions/self`, { comment });
    await invalidateAssignment(cid, aid);
    return result;
  }
  async function invalidateAssignment(cid, aid) {
    await Promise.all([C.invalidate(`assignment:${cid}:${aid}`), C.invalidate(`submission:${cid}:${aid}`), C.invalidate(`assignments:${cid}`), C.invalidate(`agroups:${cid}`), C.invalidate(`ctodo:${cid}`), invalidatePlanner()]);
  }

  /** Submitted ÷ total assignments for the progress bars. */
  async function progress(id) {
    try {
      // what can be handed in and is not excused: paper, in-class and ungraded items would keep the bar from ever filling
      const list = (await assignments(id) || []).filter((a) => a.published !== false && !a.submission?.excused && !(a.submission_types || []).some((t) => ['none', 'on_paper', 'not_graded'].includes(t)));
      const total = list.length;
      const done = list.filter((a) => ['submitted', 'graded', 'pending_review'].includes(a.submission?.workflow_state) && (a.submission?.submitted_at || a.submission?.workflow_state === 'graded')).length;
      return { done, total };
    } catch {
      return { done: 0, total: 0 };
    }
  }
  function announcements(id, { force = false, refresh = false, kind = 'courses' } = {}) {
    return C.cached(`ann:${kind}:${id}`, 5 * MIN, () =>
      C.get(`/api/v1/${kind}/${id}/discussion_topics`, { params: { only_announcements: true, per_page: 50, include: ['sections'] }, all: true, maxPages: 2 }), { force, refresh }); // (sections: which sections a section-only post went to)
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
    /** The attempt's questions, merged with their full text and answers. A quiz set to one question at
     *  a time refuses this listing ("Cannot receive one question at a time questions in the API"); for
     *  a finished attempt its question set is still readable from the quiz's own questions endpoint,
     *  scoped to that attempt (once the results are visible), without the answers — those come from
     *  the graded history the caller holds. */
    async questions(sub, { courseId = null, quizId = null } = {}) {
      try {
        const r = await C.get(`/api/v1/quiz_submissions/${sub.id}/questions`, { params: { include: ['quiz_question'] } });
        const full = new Map((r?.quiz_questions || []).map((q) => [String(q.id), q]));
        return (r?.quiz_submission_questions || []).map((q) => ({ ...(full.get(String(q.id)) || {}), ...q, id: String(q.id) }))
          .sort((a, b) => (a.position || 0) - (b.position || 0));
      } catch (e) {
        if (!courseId || !/one question at a time/i.test(e.message || '')) throw e;
        const qs = await C.get(`/api/v1/courses/${courseId}/quizzes/${quizId || sub.quiz_id}/questions`, { params: { quiz_submission_id: sub.id, quiz_submission_attempt: sub.attempt, per_page: 50 }, all: true, maxPages: 4 });
        return (Array.isArray(qs) ? qs : []).map((q) => ({ ...q, id: String(q.id), answer: null, flagged: false }))
          .sort((a, b) => (a.position || 0) - (b.position || 0));
      }
    },
    // (a quiz with an access code wants it on every call about the attempt, not only at the start)
    answer(sub, questionId, answer, accessCode) {
      return C.post(`/api/v1/quiz_submissions/${sub.id}/questions`, { attempt: sub.attempt, validation_token: sub.validation_token, ...(accessCode ? { access_code: accessCode } : {}), quiz_questions: [{ id: questionId, answer }] });
    },
    flag(sub, questionId, on, accessCode) {
      return C.put(`/api/v1/quiz_submissions/${sub.id}/questions/${questionId}/${on ? 'flag' : 'unflag'}`, { attempt: sub.attempt, validation_token: sub.validation_token, ...(accessCode ? { access_code: accessCode } : {}) });
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
  /** The module item an assignment (or page, quiz…) is, with its completion requirement. Canvas's
   *  own module_item_sequence first (what its page reads for "Mark as done"). Where that names no
   *  item, or an item without its requirement — a live Canvas answers either way, and a graded
   *  discussion or a quiz sits in its module as the topic or the quiz, not as its assignment — the
   *  modules themselves are read, and the item found there by its id (the sequence's, or the
   *  module_item_id Canvas puts in a link from the Modules page), by what it holds (the assignment,
   *  its quiz, its topic), or by its link. Null when the item is in no module, or Canvas does not
   *  answer. */
  async function moduleItemFor(id, type, assetId, { force = false, asset = null, itemId = null } = {}) {
    const fromSeq = await C.cached(`modseq:${id}:${type}:${assetId}`, 2 * MIN, () =>
      C.get(`/api/v1/courses/${id}/module_item_sequence`, { params: { asset_type: type, asset_id: assetId } })
        .then((r) => (r?.items || [])[0]?.current || null).catch(() => null), { force });
    if (fromSeq?.completion_requirement && (!itemId || String(fromSeq.id) === String(itemId))) return fromSeq;
    let mods = null;
    try { mods = await modules(id, { force }); } catch { return fromSeq; }
    const flat = (mods || []).flatMap((m) => (m.items || []).map((it) => ({ ...it, module_id: it.module_id || m.id })));
    const kinds = { Assignment: 'assignments', Quiz: 'quizzes', Page: 'pages', Discussion: 'discussion_topics', File: 'files' };
    const keys = [[type, String(assetId)]];
    if (asset?.quiz_id) keys.push(['Quiz', String(asset.quiz_id)]);
    if (asset?.discussion_topic?.id) keys.push(['Discussion', String(asset.discussion_topic.id)]);
    const links = keys.map(([t, k]) => new RegExp(`/${kinds[t] || t}/${k.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(?:[/?#]|$)`));
    const holds = (it) => keys.some(([t, k]) => it.type === t && (String(it.content_id) === k || (t === 'Page' && it.page_url === k))) || links.some((re) => re.test(it.html_url || '')); // a Page item carries its url, not a content id
    const same = (it, other) => other != null && String(it.id) === String(other);
    const cands = flat.filter((it) => same(it, itemId) || same(it, fromSeq?.id) || holds(it));
    const best = cands.find((it) => same(it, itemId)) || cands.find((it) => it.completion_requirement?.type === 'must_mark_done') || cands.find((it) => it.completion_requirement) || cands[0];
    if (!best) return fromSeq;
    return { ...(fromSeq || {}), ...best, module_id: best.module_id || fromSeq?.module_id };
  }
  /** Mark a module item done, or take that back: the same call Canvas's own "Mark as done" makes.
   *  The item's module and the sequence it came from are read again afterwards, so every screen
   *  that counts completed items sees it. */
  async function markItemDone(id, mid, iid, done, { type = 'Assignment', assetId = null } = {}) {
    if (done) await C.put(`/api/v1/courses/${id}/modules/${mid}/items/${iid}/done`, {});
    else await C.del(`/api/v1/courses/${id}/modules/${mid}/items/${iid}/done`);
    await Promise.all([C.invalidate(`modules:${id}`), assetId ? C.invalidate(`modseq:${id}:${type}:${assetId}`) : null, invalidatePlanner()]);
  }
  /** The assignments either side of one, in the order the Assignments tab lists them: by due date,
   *  the undated last, and by name between equals. From the groups already loaded for the tab. */
  function assignmentNeighbours(groups, aid) {
    const t = (a) => U.parse(a.due_at);
    const list = (groups || []).flatMap((g) => g.assignments || []).filter((a) => a.published !== false)
      .sort((x, y) => ((t(x) || Infinity) - (t(y) || Infinity)) || String(x.name || '').localeCompare(String(y.name || '')));
    const i = list.findIndex((a) => String(a.id) === String(aid));
    return i < 0 ? { prev: null, next: null } : { prev: list[i - 1] || null, next: list[i + 1] || null };
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
        const graded = s.workflow_state === 'graded' && s.score !== null && s.score !== undefined && !s.excused && s.posted_at !== null; // (a mark the teacher holds back is not a grade yet)
        const key = String(a.id);
        const raw = whatIf?.[key];
        const hypNum = whatIfOn && raw !== undefined && raw !== '' ? Number(raw) : NaN;
        const hyp = Number.isFinite(hypNum) ? hypNum : null; // ("." or "1.2.3" typed: no score, not NaN through every ring)
        const cleared = whatIfOn && (raw === '' || (raw !== undefined && !Number.isFinite(hypNum)));
        const effective = hyp !== null ? hyp : (cleared ? null : (graded ? Number(s.score) : null));
        let badgeText = '';
        if (a.omit_from_final_grade) badgeText = 'Not counted toward final grade';
        else if (s.excused) badgeText = 'Excused';
        else if (s.missing) badgeText = 'Missing';
        else if (s.late) badgeText = 'Late';
        rows.push({
          id: key, name: a.name, group: g.name, groupId: g.id, possible: Number(a.points_possible) || 0, earned: graded ? Number(s.score) : null,
          counted: !a.omit_from_final_grade && !s.excused, due: a.due_at, submitted: s.submitted_at, badge: badgeText, gradingType: a.grading_type,
          grade: graded && s.grade !== null && s.grade !== undefined && a.grading_type && a.grading_type !== 'points' ? String(s.grade) : null, // (a letter, pass/fail, percent: the grade as Canvas names it)
          stats: graded && a.score_statistics ? a.score_statistics : null, // (the class's mean, high and low)
          hypothetical: hyp !== null && (!graded || hyp !== Number(s.score)), effective, url: a.html_url, graded,
        });
      }
    }
    rows.sort((a, b) => (U.parse(a.due)?.getTime() || Infinity) - (U.parse(b.due)?.getTime() || Infinity));
    // Groups that carry weight come first (in Canvas's order), then the 0% ones.
    const ordered = [...groups].sort((a, b) => (weighted ? Number(b.weight > 0) - Number(a.weight > 0) : 0) || (a.position || 0) - (b.position || 0));
    const GROUP_COLORS = ['#0a84ff', '#5856d6', '#ff2d55', '#ff9500', '#30b0c7', '#af52de', '#ff6b22', '#c8901c'];
    // A group's rules (drop the lowest N, the highest N, never drop these) are applied the way
    // Canvas applies them: the scored items sorted by their fraction, the drops taken from the
    // ends, the never-drop ones held. Without this a group with a dropped zero read too low.
    const applyRules = (items, rules) => {
      const dropLow = Math.max(0, Number(rules?.drop_lowest) || 0);
      const dropHigh = Math.max(0, Number(rules?.drop_highest) || 0);
      if (!dropLow && !dropHigh) return { kept: items, dropped: [] };
      const never = new Set((rules?.never_drop || []).map(String));
      const cands = items.filter((r) => !never.has(String(r.id))).sort((a, b) => (a.effective / a.possible) - (b.effective / b.possible));
      const low = cands.slice(0, Math.min(dropLow, Math.max(0, cands.length - 1)));
      const rest = cands.slice(low.length);
      const high = dropHigh ? rest.slice(Math.max(0, rest.length - Math.min(dropHigh, Math.max(0, rest.length - 1)))) : [];
      const dropped = new Set([...low, ...high]);
      return { kept: items.filter((r) => !dropped.has(r)), dropped: [...dropped] };
    };
    const groupStats = ordered.map((g, i) => {
      const scoredItems = rows.filter((r) => r.groupId === g.id && r.counted && r.possible > 0 && r.effective !== null);
      const { kept: items, dropped } = applyRules(scoredItems, g.rules);
      for (const r of dropped) r.dropped = true;
      const omitted = rows.filter((r) => r.groupId === g.id && !r.counted).map((r) => r.name);
      const earned = items.reduce((s, r) => s + r.effective, 0);
      const possible = items.reduce((s, r) => s + r.possible, 0);
      return {
        ...g, color: GROUP_COLORS[i % GROUP_COLORS.length], graded: items.length > 0, earned, possible, omitted, dropped: dropped.map((r) => r.name),
        pct: possible > 0 ? Math.round((earned / possible) * 100) : null, hypothetical: items.some((r) => r.hypothetical), zero: weighted && g.weight === 0,
      };
    });
    const scored = groupStats.filter((g) => g.graded);
    const round1 = (x) => Math.round(x * 10) / 10; // (one decimal, as Canvas shows a total: 89.5 is not 90)
    let total = null;
    let note = '';
    if (weighted) {
      const denom = scored.reduce((s, g) => s + g.weight, 0);
      total = denom > 0 ? round1(scored.reduce((s, g) => s + g.weight * g.pct, 0) / denom) : null;
      note = denom > 0 ? 'Weighted across the groups that have graded work.' : 'No weighted group has graded work yet.';
    } else {
      const earned = scored.reduce((s, g) => s + g.earned, 0);
      const possible = scored.reduce((s, g) => s + g.possible, 0);
      total = possible > 0 ? round1((earned / possible) * 100) : null;
      note = possible > 0 ? `${fmtPts(earned)} / ${fmtPts(possible)} pts, graded work only.` : 'Nothing graded yet.';
    }
    if (whatIfOn) note = `What-if ${note.charAt(0).toLowerCase()}${note.slice(1)}`;
    const canvasTotal = courseInfo.score !== null && courseInfo.score !== undefined ? round1(Number(courseInfo.score)) : null;
    if (!whatIfOn && canvasTotal !== null) {
      total = canvasTotal;
      note = `As shown in Canvas${courseInfo.grade ? ` · ${courseInfo.grade}` : ''}. ${weighted ? 'Weighted across the groups that have graded work.' : 'Graded work only.'}`;
    } else if (!whatIfOn && courseInfo.hideFinal) {
      note = `${total === null ? 'Nothing graded yet. ' : ''}Canvas hides the total for this course${total === null ? '' : ': this one is worked out from the graded work'}.`;
    }
    // what the term would end on today, with every ungraded piece counted as zero (Canvas's "final" score)
    const finalScore = !whatIfOn && courseInfo.finalScore !== null && courseInfo.finalScore !== undefined && !courseInfo.hideFinal ? round1(Number(courseInfo.finalScore)) : null;
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
      if (g.dropped?.length) bits.push(g.dropped.length === 1 ? `${g.dropped[0]} dropped` : `${g.dropped.length} lowest dropped`);
      else if (g.rules?.drop_lowest) bits.push(`Canvas drops lowest ${g.rules.drop_lowest}`);
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
      center: { label: whatIfOn ? 'What-if total' : 'Total', value: total === null ? '—' : `${fmtPts(total)}%`, color: whatIfOn ? gray[0] : '#34c759', note, final: finalScore !== null && (total === null || finalScore !== total) ? `Final so far ${fmtPts(finalScore)}%${courseInfo.finalGrade ? ` · ${courseInfo.finalGrade}` : ''} — ungraded work counted as zero` : '' },
      weightSum: bearing.reduce((s, g) => s + g.weight, 0),
      weights: groups.map((g) => ({ name: g.name, pct: weighted ? `${g.weight}%` : '—', zero: weighted && g.weight === 0 })),
    };
  }
  function fmtPts(n) {
    return Number.isInteger(n) ? String(n) : Number(n).toFixed(1).replace(/\.0$/, '');
  }
  /** Where a planner item stands (a To Do row, the Dashboard's list, the phone's), in the same words
   *  workStatus() uses for an assignment: [{ word, kind }] — the state first (Excused, Graded,
   *  Submitted, Late, Missing), then what is new (Feedback, Redo, New). Nothing for plain open work
   *  that is not yet due: the row's date says that. */
  function workFlags(it) {
    const out = [];
    if (!it || it.custom) return out;
    if (it.excused) out.push({ word: 'Excused', kind: 'muted' });
    else if (it.graded) out.push({ word: 'Graded', kind: 'good' });
    else if (it.submitted) out.push({ word: it.late ? 'Submitted late' : 'Submitted', kind: it.late ? 'warn' : 'good' });
    else if (it.missing) out.push({ word: 'Missing', kind: 'bad' });
    else if (it.late) out.push({ word: 'Late', kind: 'warn' });
    else if (it.isDue && it.date && it.date < now()) out.push({ word: 'Missing', kind: 'bad' });
    if (it.redo) out.push({ word: 'Redo asked', kind: 'warn' });
    else if (it.feedback) out.push({ word: 'Feedback', kind: 'info' });
    if (it.newActivity && !it.feedback) out.push({ word: 'New', kind: 'info' });
    return out;
  }
  /** Where a piece of work stands, in the words every screen and the search hub use, from its
   *  assignment and its submission: { word, kind, graded } — kind is '' | 'good' | 'warn' | 'bad' |
   *  'muted' for a badge's colour. A score counts only once the teacher has posted it (posted_at
   *  null means held back, as Canvas's own pages read it); a letter, pass/fail or percent grade is
   *  the grade itself rather than points; a lock window says Opens or Closed; past due with nothing
   *  in is Missing, as Canvas's own lists say. */
  function workStatus(a, s = a?.submission) {
    const sub = s && typeof s === 'object' ? s : {};
    const now = Date.now();
    const scored = sub.workflow_state === 'graded' && sub.score !== null && sub.score !== undefined && sub.posted_at !== null;
    const types = a?.submission_types || [];
    const gradeText = () => {
      const t = a?.grading_type;
      if (sub.grade !== null && sub.grade !== undefined && t && t !== 'points') return String(sub.grade); // letter, pass/fail, complete/incomplete, GPA scale, percent
      return `${fmtPts(sub.score)}${a?.points_possible !== null && a?.points_possible !== undefined ? `/${fmtPts(a.points_possible)}` : ''}`;
    };
    if (sub.excused) return { word: 'Excused', kind: 'muted' };
    if (scored) return { word: `${gradeText()}${sub.late ? ' · late' : ''}`, kind: 'good', graded: true, late: !!sub.late };
    if (sub.submitted_at || sub.workflow_state === 'submitted' || sub.workflow_state === 'pending_review') return sub.late ? { word: 'Submitted late', kind: 'warn' } : { word: 'Submitted', kind: 'good' };
    if (sub.missing) return { word: 'Missing', kind: 'bad' };
    if (a?.unlock_at && U.parse(a.unlock_at) > now) return { word: `Opens ${U.whenShort(U.parse(a.unlock_at))}`, kind: 'muted' };
    if (a?.lock_at && U.parse(a.lock_at) < now) return { word: 'Closed', kind: 'muted' };
    if (types.includes('not_graded')) return { word: 'Not graded', kind: 'muted' };
    if (types.includes('on_paper')) return { word: 'On paper', kind: 'muted' };
    if (types.includes('none')) return { word: 'Nothing to hand in', kind: 'muted' };
    if (a?.due_at && U.parse(a.due_at) < now) return { word: 'Missing', kind: 'bad' };
    return { word: 'Not submitted', kind: '' };
  }

  BCV.store = {
    env, pref, setPref, mergePref, me, account, colors, courses, favorites, cards, setFavorite, setNickname, currentTerm, dashboardView, setDashboardView, freshness, invalidateGrades,
    planner, classify, todo, todoWindow, setComplete, dismiss, restore, invalidatePlanner, plannerOverrides, createNote, deleteNote, activity, activitySummary, unreadCount, groups, group, workStatus, workFlags,
    announcementsFeed, streamSeen, markStreamSeen, setColor, history, helpLinks,
    calendarContexts, ownContexts, selectedContexts, setSelectedContexts, calendarEvents, plannerRange, appointmentGroups, reserveAppointment, cancelReservation,
    conversations, conversation, markRead, setStarred, replyTo, compose, searchRecipients, invalidateInbox,
    course, tabs, frontPage, syllabus, courseTodo, ignoreTodo, courseStream, assignments, assignment, submission, assignmentGroups, progress,
    announcements, discussions, discussion, discussionView, postEntry, markTopicRead, people, sections, courseGroups, pages, page,
    rootFolder, folderContents, folderByPath, file, quizzes, quiz, quizSubmissions, quizApi, modules, moduleItemFor, markItemDone, assignmentNeighbours, gradeModel, fmtPts,
    notifications, notifState, setNotifState, notifUnread,
    homeworkTools, uploadSubmissionFile, uploadSubmissionFileFromUrl, submitAssignment, commentOnSubmission, invalidateAssignment, quizAttemptLimit,
  };
})();
