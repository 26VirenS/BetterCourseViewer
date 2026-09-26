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
      self.BCV?.errors?.failed?.(status); // (lib/errors.js: an error shown now takes this one's kind — Canvas's status, or NET for no answer)
    }
  }

  // ---- the gate: how many requests are out at once, and whose go first -----------------------
  // Canvas throttles by what is outstanding: every open request holds 50 units of a 700-unit
  // bucket until it answers, so past about a dozen in flight the rest are refused (a 403) rather
  // than queued. The gate keeps the app under that. It also decides the order: each request
  // carries the navigation it was made under, and the newest navigation's requests are served
  // first — so the screen being opened never waits behind what the background asked for on the
  // screen before it. Writes are never held.
  //
  // Two more things keep a press quick when the background is busy. A request made after a screen
  // has settled is a warm-up — what the next press might want, not what the user is waiting on —
  // and when every slot is held, the screen being drawn takes one from a warm-up in flight for an
  // older screen: that warm-up is stopped and asked again later, behind the screen's own requests.
  // And a warm-up the new screen turns out to want (Groups pressed while its list is still queued
  // behind the calendar's month) is moved up to the new screen's place in the queue rather than
  // left waiting its turn as a warm-up.
  const MAX_INFLIGHT = 10;
  let navigation = 0; // the app counts its navigations here
  let drawing = true; // until the screen settles, what is asked for is what the user is waiting on
  let open = 0;
  let order = 0; // the order requests went out in
  const waiting = []; // slots asked for while every slot was held: { nav, key, bg, resolve }, in the order asked
  const running = new Set(); // the slots held: { nav, key, bg, ac, at, yielded }
  let loadingKey = null; // the memo key whose loader is being called, so its requests can be found in the queue again
  const navigated = () => { navigation++; drawing = true; };
  const settled = () => { drawing = false; };
  function admit(slot) {
    if (open < MAX_INFLIGHT) {
      open++;
      running.add(slot);
      return Promise.resolve();
    }
    if (!slot.bg) {
      // every slot is held: a warm-up in flight for an older screen gives its slot up to the screen
      // being drawn — the oldest screen's, and of those the one that went out last (the least done)
      let v = null;
      for (const s of running) if (s.bg && s.nav < slot.nav && s.ac && (!v || s.nav < v.nav || (s.nav === v.nav && s.at > v.at))) v = s;
      if (v) {
        v.yielded = true;
        running.delete(v);
        v.ac.abort();
        open++;
        running.add(slot);
        return Promise.resolve();
      }
    }
    return new Promise((resolve) => { slot.resolve = resolve; waiting.push(slot); });
  }
  function release(slot) {
    running.delete(slot);
    if (slot.yielded || !waiting.length) { // (a slot given up was taken over already: nothing to pass on)
      open--;
      return;
    }
    let next = 0; // the newest navigation's first request; the slot passes straight to it
    for (let i = 1; i < waiting.length; i++) if (waiting[i].nav > waiting[next].nav) next = i;
    const s = waiting.splice(next, 1)[0];
    running.add(s);
    s.resolve();
  }
  /** The requests behind a memo key are now what the screen being drawn is waiting on. */
  function promote(key) {
    for (const s of waiting) if (s.key === key) { s.nav = navigation; s.bg = false; }
    for (const s of running) if (s.key === key) { s.nav = navigation; s.bg = false; }
  }
  const pause = (ms) => new Promise((r) => setTimeout(r, ms));

  // ---- the session ---------------------------------------------------------------------------
  // A Canvas session that has ended (signed out elsewhere, expired overnight) answers every API
  // call with a 401 "unauthenticated" or a redirect to the sign-in page. The first such answer is
  // reported to the app, which sends the page to sign in again; every request after it fails at
  // once rather than each screen finding out for itself and stalling on the way.
  let sessionLost = false;
  const sessionListeners = new Set();
  const onSessionLost = (fn) => { sessionListeners.add(fn); return () => sessionListeners.delete(fn); };
  const sessionOk = () => !sessionLost;
  const looksSignedOut = (res, text) => {
    if (res.status === 401 && /unauthenticated|authorization required|must be logged in/i.test(text || '')) return true;
    try { return !!res.redirected && /^\/login(\/|$)/.test(new URL(res.url, location.origin).pathname); } catch { return false; }
  };
  function loseSession() {
    if (sessionLost) return;
    sessionLost = true;
    for (const fn of sessionListeners) { try { fn(); } catch { /* ignore */ } }
  }
  /** Asks Canvas the cheapest question there is; false once the session has ended. */
  async function checkSession() {
    try { await request('GET', '/api/v1/users/self'); } catch { /* a lost session is reported by the request itself */ }
    return !sessionLost;
  }

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
    const slot = { nav: navigation, key: loadingKey, bg: !drawing, ac: null, at: 0, yielded: false }; // one slot, page by page
    while (url && pages < maxPages) {
      if (sessionLost) throw new CanvasError('Signed out of Canvas', 401);
      if (method === 'GET') {
        slot.ac = null;
        slot.yielded = false;
        await admit(slot);
      }
      let res, text;
      let again = false; // this page timed out (or gave its slot up) and is to be asked once more
      const ac = typeof AbortController === 'function' ? new AbortController() : null;
      slot.ac = ac;
      slot.at = ++order;
      const timer = ac ? setTimeout(() => ac.abort(), REQUEST_TIMEOUT) : null;
      try {
        // Canvas refuses any non-GET request without the CSRF token, body or not (a DELETE has none).
        // Never the browser's HTTP cache, and nothing in between is to answer from its own: a
        // score read from a cached body is a score days old (a grade a tool posted, never seen
        // until a look switch happened to reload the page) — the per-page memo above is the only
        // cache these answers live in.
        res = await fetch(url, {
          method,
          credentials: 'same-origin',
          cache: 'no-store',
          headers: {
            accept: ACCEPT,
            'cache-control': 'no-cache',
            pragma: 'no-cache',
            ...(method !== 'GET' ? { 'x-csrf-token': csrfToken() } : {}),
            ...(body ? { 'content-type': 'application/json' } : {}),
            'x-requested-with': 'XMLHttpRequest',
          },
          body: body ? JSON.stringify(body) : undefined,
          signal: ac ? ac.signal : undefined,
        });
        text = await res.text();
      } catch (e) {
        if (!(ac && ac.signal.aborted)) { self.BCV?.errors?.failed?.('NET'); throw e; } // (no answer at all: offline, or the connection dropped)
        if (slot.yielded) {
          again = true; // its slot went to the screen being drawn: asked again once a slot is free
        } else {
          if (method !== 'GET' || timedOut >= 1) throw new CanvasError('Canvas did not answer in time', 0);
          timedOut++;
          again = true;
        }
      } finally {
        clearTimeout(timer);
        if (method === 'GET') release(slot);
      }
      if (again) continue;
      if (looksSignedOut(res, text)) {
        loseSession();
        throw new CanvasError('Signed out of Canvas', 401);
      }
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
  // A caller that must not draw from an old answer (a score: a tool may have posted a grade since)
  // asks with maxAge — an answer older than that is fetched again, whatever its TTL.
  const memory = new Map(); // key → { value, at, until }
  const inflight = new Map();
  const generation = new Map();
  const cacheKey = (key) => `${location.host}:${key}`;
  /** The memo is bounded: a long session in one tab (every course's every tab warmed, then the
   *  next course's) would otherwise hold every answer it ever had. Past the cap the expired go
   *  first, then the oldest; nothing on screen is lost, since a screen holds what it drew from. */
  const MEMO_MAX = 400;
  function trim() {
    if (memory.size <= MEMO_MAX) return;
    const now = Date.now();
    for (const [k, v] of memory) if (v.until && v.until <= now) memory.delete(k);
    if (memory.size <= MEMO_MAX) return;
    const oldest = [...memory.entries()].sort((a, b) => (a[1].at || 0) - (b[1].at || 0)).slice(0, memory.size - MEMO_MAX);
    for (const [k] of oldest) memory.delete(k);
  }
  async function cached(key, ttlMs, loader, { force = false, refresh = false, maxAge = 0 } = {}) {
    const k = cacheKey(key);
    if (!force && !refresh) {
      const hit = memory.get(k);
      const fresh = hit && (!hit.until || hit.until > Date.now()) && !(maxAge > 0 && Date.now() - (hit.at || 0) > maxAge);
      if (fresh) return hit.value;
      if (inflight.has(k)) {
        if (drawing) promote(k); // a warm-up still queued that the screen being drawn wants: it goes up to the screen's place
        return inflight.get(k);
      }
    }
    const gen = generation.get(k) || 0;
    // the loader's own requests (the ones it makes before its first await, which is all of them for
    // nearly every loader) are made while the key is set, so they can be found in the queue again
    const was = loadingKey;
    loadingKey = k;
    let run;
    try {
      run = (async () => {
        const value = await loader();
        if ((generation.get(k) || 0) === gen) { memory.set(k, { value, at: Date.now(), until: ttlMs > 0 ? Date.now() + ttlMs : 0 }); trim(); }
        return value;
      })();
    } finally {
      loadingKey = was;
    }
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
    inflight.delete(k); // a run still answering answers whoever awaited it, but nobody new joins it: after a write, the next caller asks afresh
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


  BCV.canvas = {
    get, post, put, del, upload, cached, ready, invalidate, invalidatePrefix, clearAll, navigated, settled, tune, onSessionLost, sessionOk, checkSession, csrfToken, CanvasError,
  };
})();
