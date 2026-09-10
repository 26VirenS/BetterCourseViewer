/* Same-origin Canvas REST helper for content scripts.
 * Uses the logged-in session (cookies); strips Canvas's `while(1);` prefix,
 * follows Link-header pagination, and caches responses in storage.local. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const api = BCV.api;

  const ACCEPT = 'application/json+canvas-string-ids';

  function csrfToken() {
    const meta = document.querySelector('meta[name="csrf-token"]');
    if (meta?.content) return meta.content;
    const m = document.cookie.match(/(?:^|;\s*)_csrf_token=([^;]+)/);
    return m ? decodeURIComponent(m[1]) : '';
  }

  function buildUrl(path, params) {
    const url = new URL(path, location.origin);
    if (params) {
      for (const [k, v] of Object.entries(params)) {
        if (v === undefined || v === null) continue;
        if (Array.isArray(v)) v.forEach((x) => url.searchParams.append(k.endsWith('[]') ? k : `${k}[]`, x));
        else if (k.endsWith('[]')) url.searchParams.append(k, v);
        else url.searchParams.set(k, v);
      }
    }
    return url.toString();
  }

  function parseBody(text) {
    const cleaned = text.replace(/^while\(1\);/, '');
    if (!cleaned.trim()) return null;
    return JSON.parse(cleaned);
  }

  function nextLink(header) {
    if (!header) return null;
    for (const part of header.split(',')) {
      const m = part.match(/<([^>]+)>;\s*rel="next"/);
      if (m) return m[1];
    }
    return null;
  }

  class CanvasError extends Error {
    constructor(message, status) {
      super(message);
      this.status = status;
    }
  }

  async function request(method, path, { params, body, all = false, maxPages = 10 } = {}) {
    let url = buildUrl(path, params);
    const results = [];
    let pages = 0;
    while (url && pages < maxPages) {
      const res = await fetch(url, {
        method,
        credentials: 'same-origin',
        headers: {
          accept: ACCEPT,
          ...(body ? { 'content-type': 'application/json', 'x-csrf-token': csrfToken() } : {}),
          'x-requested-with': 'XMLHttpRequest',
        },
        body: body ? JSON.stringify(body) : undefined,
      });
      const text = await res.text();
      if (!res.ok) {
        let msg = `Canvas API ${res.status}`;
        try {
          const j = parseBody(text);
          msg = j?.errors?.[0]?.message || j?.message || msg;
        } catch {
          /* ignore */
        }
        throw new CanvasError(msg, res.status);
      }
      const data = parseBody(text);
      pages++;
      if (!all) return data;
      if (Array.isArray(data)) results.push(...data);
      else return data;
      url = nextLink(res.headers.get('link'));
    }
    return results;
  }

  const get = (path, opts) => request('GET', path, opts);
  const post = (path, body, opts) => request('POST', path, { ...opts, body });
  const put = (path, body, opts) => request('PUT', path, { ...opts, body });
  const del = (path, opts) => request('DELETE', path, opts);

  // ---- cache -------------------------------------------------------------
  const memory = new Map();
  const cacheKey = (key) => `cache:${location.host}:${key}`;

  async function cached(key, ttlMs, loader, { force = false } = {}) {
    const k = cacheKey(key);
    const now = Date.now();
    if (!force) {
      const mem = memory.get(k);
      if (mem && mem.expires > now) return mem.value;
      try {
        const stored = await api.storage.local.get(k);
        const hit = stored[k];
        if (hit && hit.expires > now) {
          memory.set(k, hit);
          return hit.value;
        }
      } catch {
        /* ignore */
      }
    }
    const value = await loader();
    const entry = { value, expires: now + ttlMs };
    memory.set(k, entry);
    try {
      await api.storage.local.set({ [k]: entry });
    } catch {
      /* ignore quota errors */
    }
    return value;
  }

  async function invalidate(key) {
    const k = cacheKey(key);
    memory.delete(k);
    try {
      await api.storage.local.remove(k);
    } catch {
      /* ignore */
    }
  }

  // ---- higher-level helpers ---------------------------------------------
  const MIN = 60e3;

  function isoDaysFromNow(days) {
    const d = new Date();
    d.setDate(d.getDate() + days);
    return d.toISOString();
  }

  /** Planner items for the window [-7d, +lookahead], cached 5 minutes. */
  function plannerItems(lookaheadDays = 14, { force = false } = {}) {
    return cached(`planner:${lookaheadDays}`, 5 * MIN, () =>
      get('/api/v1/planner/items', {
        params: { start_date: isoDaysFromNow(-7), end_date: isoDaysFromNow(lookaheadDays), per_page: 100 },
        all: true,
        maxPages: 5,
      }), { force });
  }

  /** Dashboard cards (favourite courses in dashboard order), cached 15 minutes. */
  function dashboardCards({ force = false } = {}) {
    return cached('dashboard-cards', 15 * MIN, () => get('/api/v1/dashboard/dashboard_cards'), { force });
  }

  /** Active courses, cached 30 minutes. */
  function activeCourses({ force = false } = {}) {
    return cached('courses', 30 * MIN, () =>
      get('/api/v1/courses', {
        params: { enrollment_state: 'active', per_page: 100, include: ['term', 'favorites'] },
        all: true,
      }), { force });
  }

  /** Custom course colours, cached 1 hour. */
  function courseColors({ force = false } = {}) {
    return cached('colors', 60 * MIN, async () => {
      try {
        const r = await get('/api/v1/users/self/colors');
        return r?.custom_colors || {};
      } catch {
        return {};
      }
    }, { force });
  }

  /** Active courses with current scores and term, cached 15 minutes. */
  function coursesWithScores({ force = false } = {}) {
    return cached('courses-scores', 15 * MIN, () =>
      get('/api/v1/courses', {
        params: { enrollment_state: 'active', per_page: 100, include: ['total_scores', 'term', 'favorites', 'teachers', 'course_image'] },
        all: true,
      }), { force });
  }

  /** Navigation tabs the user can see in a course, cached 30 minutes. */
  function courseTabs(courseId, { force = false } = {}) {
    return cached(`tabs:${courseId}`, 30 * MIN, () => get(`/api/v1/courses/${courseId}/tabs`), { force });
  }

  /** One course with teachers, term, scores and syllabus, cached 15 minutes. */
  function course(courseId, { force = false } = {}) {
    return cached(`course:${courseId}`, 15 * MIN, () =>
      get(`/api/v1/courses/${courseId}`, { params: { include: ['teachers', 'term', 'total_scores', 'syllabus_body', 'course_image'] } }), { force });
  }

  /** Modules with items (for progress), cached 10 minutes. */
  function courseModules(courseId, { force = false } = {}) {
    return cached(`modules:${courseId}`, 10 * MIN, () =>
      get(`/api/v1/courses/${courseId}/modules`, { params: { include: ['items'], per_page: 50 }, all: true, maxPages: 3 }), { force });
  }

  /** Recent announcements across the given courses (last 21 days), cached 10 minutes. */
  function announcements(courseIds, { force = false } = {}) {
    const ids = (courseIds || []).slice(0, 20);
    if (!ids.length) return Promise.resolve([]);
    return cached(`announcements:${ids.join(',')}`, 10 * MIN, () =>
      get('/api/v1/announcements', {
        params: { 'context_codes[]': ids.map((id) => `course_${id}`), start_date: isoDaysFromNow(-21), end_date: isoDaysFromNow(1), per_page: 30 },
      }), { force });
  }

  /** Unread inbox count, cached 2 minutes. */
  function unreadCount({ force = false } = {}) {
    return cached('unread', 2 * MIN, async () => {
      try {
        const r = await get('/api/v1/conversations/unread_count');
        return Number(r?.unread_count) || 0;
      } catch {
        return 0;
      }
    }, { force });
  }

  /** Mark a planner item complete/incomplete (syncs with Canvas's own To Do). */
  async function setPlannerComplete(item, complete) {
    const type = item.plannable_type;
    const id = item.plannable_id;
    const override = item.planner_override;
    let result;
    if (override && override.id) {
      result = await put(`/api/v1/planner/overrides/${override.id}`, { marked_complete: complete });
    } else {
      result = await post('/api/v1/planner/overrides', { plannable_type: type, plannable_id: id, marked_complete: complete });
    }
    await invalidate('planner:14');
    return result;
  }

  BCV.canvas = {
    get, post, put, del, cached, invalidate, csrfToken, CanvasError,
    plannerItems, dashboardCards, activeCourses, courseColors, setPlannerComplete,
    coursesWithScores, courseTabs, course, courseModules, announcements, unreadCount,
  };
})();
