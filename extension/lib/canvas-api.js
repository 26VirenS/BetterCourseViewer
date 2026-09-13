/* Same-origin Canvas REST helper for content scripts.
 * Uses the logged-in session (cookies); strips Canvas's `while(1);` prefix,
 * follows Link-header pagination, and caches responses in storage.local. */
(function () {
  const BCV = (self.BCV = self.BCV || {});

  const ACCEPT = 'application/json+canvas-string-ids';

  /** The session's CSRF token, the way Canvas's own front end reads it: the _csrf_token cookie
   *  (URL-encoded, refreshed by Canvas as it goes), with a csrf-token meta tag as the fallback. */
  function csrfToken() {
    const m = document.cookie.match(/(?:^|;\s*)_csrf_token=([^;]+)/);
    if (m) return decodeURIComponent(m[1]);
    const meta = document.querySelector('meta[name="csrf-token"]');
    return meta?.content || '';
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

  // ---- the gate: how many requests are out at once, and whose go first -----------------------
  // Canvas throttles by what is outstanding: every open request holds 50 units of a 700-unit
  // bucket until it answers, so past about a dozen in flight the rest are refused (a 403) rather
  // than queued. The gate keeps the app under that. It also decides the order: each request
  // carries the navigation it was made under, and the newest navigation's requests are served
  // first — so the screen being opened never waits behind what the background asked for on the
  // screen before it. Writes are never held.
  const MAX_INFLIGHT = 10;
  let navigation = 0; // the app counts its navigations here
  let open = 0;
  const waiting = []; // { nav, resolve }, in the order asked
  const navigated = () => { navigation++; };
  function admit() {
    if (open < MAX_INFLIGHT) {
      open++;
      return Promise.resolve();
    }
    return new Promise((resolve) => waiting.push({ nav: navigation, resolve }));
  }
  function release() {
    if (!waiting.length) {
      open--;
      return;
    }
    let next = 0; // the newest navigation's first request; the slot passes straight to it
    for (let i = 1; i < waiting.length; i++) if (waiting[i].nav > waiting[next].nav) next = i;
    waiting.splice(next, 1)[0].resolve();
  }
  const pause = (ms) => new Promise((r) => setTimeout(r, ms));

  // A request that never answers would hold its gate slot and its screen for good: after this
  // long it is given up, a GET is asked once more, and then it fails like any other request (the
  // screen shows its error, or gives way, rather than waiting forever).
  let REQUEST_TIMEOUT = 20000;
  const tune = ({ requestTimeout } = {}) => { if (Number.isFinite(requestTimeout)) REQUEST_TIMEOUT = requestTimeout; };

  async function request(method, path, { params, body, all = false, maxPages = 10 } = {}) {
    let url = buildUrl(path, params);
    const results = [];
    let pages = 0;
    let throttled = 0;
    let timedOut = 0;
    while (url && pages < maxPages) {
      if (method === 'GET') await admit();
      let res, text;
      let again = false; // this page timed out and is to be asked once more
      const ac = typeof AbortController === 'function' ? new AbortController() : null;
      const timer = ac ? setTimeout(() => ac.abort(), REQUEST_TIMEOUT) : null;
      try {
        // Canvas refuses any non-GET request without the CSRF token, body or not (a DELETE has none).
        res = await fetch(url, {
          method,
          credentials: 'same-origin',
          headers: {
            accept: ACCEPT,
            ...(method !== 'GET' ? { 'x-csrf-token': csrfToken() } : {}),
            ...(body ? { 'content-type': 'application/json' } : {}),
            'x-requested-with': 'XMLHttpRequest',
          },
          body: body ? JSON.stringify(body) : undefined,
          signal: ac ? ac.signal : undefined,
        });
        text = await res.text();
      } catch (e) {
        if (!(ac && ac.signal.aborted)) throw e;
        if (method !== 'GET' || timedOut >= 1) throw new CanvasError('Canvas did not answer in time', 0);
        timedOut++;
        again = true;
      } finally {
        clearTimeout(timer);
        if (method === 'GET') release();
      }
      if (again) continue;
      if (!res.ok) {
        // the bucket ran dry all the same (Canvas's own page traffic counts against it too): this
        // is not a refusal of the thing asked for, so the page is asked for again after a moment
        if (res.status === 403 && /rate limit/i.test(text) && throttled < 2) {
          throttled++;
          await pause(700 * throttled);
          continue;
        }
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

  /** Step 2 of a Canvas file upload: POST the multipart form to the storage
   *  URL the preflight returned (Canvas itself, inst-fs or S3). XHR so the
   *  progress events reach the row. Redirects are followed by the browser;
   *  a JSON body with `id` is the finished file, `location` is the confirm
   *  URL to GET (step 3). */
  function upload(url, form, { onProgress = null } = {}) {
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      xhr.open('POST', url);
      let sameOrigin = false;
      try {
        sameOrigin = new URL(url, location.origin).origin === location.origin;
      } catch {
        /* ignore */
      }
      xhr.withCredentials = sameOrigin; // S3 / inst-fs answer with a wildcard CORS header, which forbids credentials
      xhr.setRequestHeader('accept', 'application/json');
      if (xhr.upload && onProgress) xhr.upload.addEventListener('progress', (e) => { if (e.lengthComputable) onProgress(e.loaded / e.total); });
      xhr.addEventListener('load', () => {
        if (xhr.status < 200 || xhr.status >= 400) return reject(new CanvasError(`Upload failed (${xhr.status})`, xhr.status));
        try {
          resolve(parseBody(xhr.responseText || ''));
        } catch {
          resolve(null);
        }
      });
      xhr.addEventListener('error', () => reject(new CanvasError('Upload failed: the connection dropped', 0)));
      xhr.addEventListener('abort', () => reject(new CanvasError('Upload cancelled', 0)));
      xhr.send(form);
    });
  }

  // ---- per-page memo ---------------------------------------------------------------------
  // Every page load (or reload) starts with an empty memo and asks Canvas afresh. Within one page
  // a key is requested once and shared by every component that wants it (callers that arrive
  // while it runs share the request); the interface then navigates in place, so an answer lives
  // for its TTL and is fetched again after that. Nothing is kept between pages.
  // A write invalidates a key; a request for that key that was already running when the write
  // happened answers its caller but never lands in the memo (it would put the stale list back).
  const memory = new Map(); // key → { value, until }
  const inflight = new Map();
  const generation = new Map();
  const cacheKey = (key) => `${location.host}:${key}`;
  async function cached(key, ttlMs, loader, { force = false, refresh = false } = {}) {
    const k = cacheKey(key);
    if (!force && !refresh) {
      const hit = memory.get(k);
      if (hit && (!hit.until || hit.until > Date.now())) return hit.value;
      if (inflight.has(k)) return inflight.get(k);
    }
    const gen = generation.get(k) || 0;
    const run = (async () => {
      const value = await loader();
      if ((generation.get(k) || 0) === gen) memory.set(k, { value, until: ttlMs > 0 ? Date.now() + ttlMs : 0 });
      return value;
    })();
    inflight.set(k, run);
    try {
      return await run;
    } finally {
      if (inflight.get(k) === run) inflight.delete(k);
    }
  }
  /** What is already in the memo for this key, or undefined — never a request. For the parts of a
   *  screen that are worth drawing with if the answer happens to be here, and not worth waiting for
   *  if it is not (a course's name beside a group, say). */
  function ready(key) {
    const hit = memory.get(cacheKey(key));
    return hit && (!hit.until || hit.until > Date.now()) ? hit.value : undefined;
  }
  const forget = (k) => {
    memory.delete(k);
    generation.set(k, (generation.get(k) || 0) + 1);
  };
  async function invalidate(key) {
    forget(cacheKey(key));
  }
  /** Forget every memoised (or still loading) key that starts with the prefix (after a write). */
  async function invalidatePrefix(prefix) {
    const p = cacheKey(prefix);
    for (const k of new Set([...memory.keys(), ...inflight.keys()])) if (k.startsWith(p)) forget(k);
  }
  /** Forget everything (a page brought back from the back/forward cache, a re-tap of the current
   *  tab): the next draw asks Canvas afresh, exactly as a fresh page load does. */
  function clearAll() {
    for (const k of new Set([...memory.keys(), ...inflight.keys()])) forget(k);
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
    get, post, put, del, upload, cached, ready, invalidate, invalidatePrefix, clearAll, navigated, tune, csrfToken, CanvasError,
    plannerItems, dashboardCards, activeCourses, courseColors, setPlannerComplete,
    coursesWithScores, courseTabs, course, courseModules, announcements, unreadCount,
  };
})();
