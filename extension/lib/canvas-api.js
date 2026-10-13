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
  //
  // (2.99.33) What the app reads ahead of a press — at idle, or under a pointer resting on a link — is
  // a warm-up of its own kind (`pre`): never more than MAX_WARM of those out at once, asked for at the
  // browser's low priority, and the app stands them down when Canvas says its bucket is running low
  // (budget(), from the X-Rate-Limit-Remaining header every answer carries). A navigation lifts the
  // cap off any still queued: they wait behind the new screen's own requests, as any warm-up does.
  const MAX_INFLIGHT = 10;
  const MAX_WARM = 2;
  let navigation = 0; // the app counts its navigations here
  let drawing = true; // until the screen settles, what is asked for is what the user is waiting on
  let warmingNow = false; // the app is reading ahead (warming()): what is asked for now is a `pre` warm-up
  let open = 0;
  let warmOpen = 0; // of those, the `pre` warm-ups
  let order = 0; // the order requests went out in
  const waiting = []; // slots asked for while every slot was held: { nav, key, bg, pre, resolve }, in the order asked
  const running = new Set(); // the slots held: { nav, key, bg, pre, ac, at, yielded }
  let loadingKey = null; // the memo key whose loader is being called, so its requests can be found in the queue again
  let budget = { remaining: null, at: 0 }; // Canvas's own count of what is left in the bucket, as of its last answer
  const navigated = () => {
    navigation++;
    drawing = true;
    warmingNow = false;
    for (const s of waiting) s.pre = false; // (still queued from the screen before: behind this one's, with no cap of their own)
    pump();
  };
  const settled = () => { drawing = false; };
  /** The app reading ahead (true), or done or interrupted (false): see `pre` above. */
  const warming = (on) => { warmingNow = !!on; };
  const startable = (s) => !s.pre || warmOpen < MAX_WARM;
  function begin(s) {
    open++;
    s.held = true;
    s.heldPre = !!s.pre;
    if (s.heldPre) warmOpen++;
    running.add(s);
  }
  function free(s) {
    running.delete(s);
    if (!s.held) return;
    s.held = false;
    open--;
    if (s.heldPre) warmOpen--;
  }
  function admit(slot) {
    if (open < MAX_INFLIGHT && startable(slot)) {
      begin(slot);
      return Promise.resolve();
    }
    if (!slot.bg && open >= MAX_INFLIGHT) {
      // every slot is held: a warm-up in flight for an older screen gives its slot up to the screen
      // being drawn — the oldest screen's, and of those the one that went out last (the least done)
      let v = null;
      for (const s of running) if (s.bg && s.nav < slot.nav && s.ac && (!v || s.nav < v.nav || (s.nav === v.nav && s.at > v.at))) v = s;
      if (v) {
        v.yielded = true;
        free(v);
        v.ac.abort();
        begin(slot);
        return Promise.resolve();
      }
    }
    return new Promise((resolve) => { slot.resolve = resolve; waiting.push(slot); });
  }
  /** Hands free slots on: to the newest navigation's first request that may start (a `pre` one only while fewer than MAX_WARM are out). */
  function pump() {
    while (open < MAX_INFLIGHT && waiting.length) {
      let next = -1;
      for (let i = 0; i < waiting.length; i++) if (startable(waiting[i]) && (next < 0 || waiting[i].nav > waiting[next].nav)) next = i;
      if (next < 0) return;
      const s = waiting.splice(next, 1)[0];
      begin(s);
      s.resolve();
    }
  }
  function release(slot) {
    free(slot); // (a slot given up was freed when it was taken over: nothing more to give back)
    pump();
  }
  /** The requests behind a memo key are now what the screen being drawn is waiting on. */
  function promote(key) {
    for (const s of waiting) if (s.key === key) { s.nav = navigation; s.bg = false; s.pre = false; }
    for (const s of running) if (s.key === key) { s.nav = navigation; s.bg = false; s.pre = false; }
    pump();
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
  let ttlScale = 1; // (a test makes the memo's answers lapse sooner: fluid-test.mjs)
  const tune = ({ requestTimeout, lifetimes } = {}) => { if (Number.isFinite(requestTimeout)) REQUEST_TIMEOUT = requestTimeout; if (Number.isFinite(lifetimes) && lifetimes > 0) ttlScale = lifetimes; };

  async function request(method, path, { params, body, all = false, maxPages = 10 } = {}) {
    // (a Brightspace page: the same request, answered by Brightspace in Canvas's shapes — lib/d2l-api.js)
    if (self.BCV?.d2l) {
      if (sessionLost) throw new CanvasError('Signed out of Brightspace', 401);
      return self.BCV.d2l.request(method, path, { params, body, all, maxPages });
    }
    let url = buildUrl(path, params);
    const results = [];
    let pages = 0;
    let throttled = 0;
    let timedOut = 0;
    const slot = { nav: navigation, key: loadingKey, bg: !drawing, pre: warmingNow && !drawing, ac: null, at: 0, yielded: false }; // one slot, page by page
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
        // until a look switch happened to reload the page) — the per-page memo below is the only
        // cache these answers live in (and what the store keeps between page loads, which is
        // always asked for again the moment it is used: below).
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
          ...(slot.pre ? { priority: 'low' } : {}), // (a read ahead: the browser's own queue puts it behind what is on screen)
        });
        const left = res.headers?.get?.('x-rate-limit-remaining');
        if (left !== null && left !== undefined && left !== '' && Number.isFinite(Number(left))) budget = { remaining: Number(left), at: Date.now() };
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
    if (/^d2l-upload:/.test(String(url)) && self.BCV?.d2l) return self.BCV.d2l.upload(url, form, { onProgress }); // (Brightspace takes the file with the hand-in itself)
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
  // for its TTL and is fetched again after that. (What a page opens with is kept between pages: below.)
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
  /** Calls the loader for a key, its answer landing in the memo (unless a write forgot the key meanwhile). */
  function start(k, key, ttlMs, loader) {
    const gen = generation.get(k) || 0;
    // the loader's own requests (the ones it makes before its first await, which is all of them for
    // nearly every loader) are made while the key is set, so they can be found in the queue again
    const was = loadingKey;
    loadingKey = k;
    let run;
    try {
      run = (async () => {
        const value = await loader();
        if ((generation.get(k) || 0) === gen) {
          memory.set(k, { value, at: Date.now(), until: ttlMs > 0 ? Date.now() + ttlMs * ttlScale : 0 });
          trim();
          try { onAnswer?.(key, value); } catch { /* not kept: the next page asks Canvas, as before */ }
        }
        return value;
      })();
    } finally {
      loadingKey = was;
    }
    inflight.set(k, run);
    const done = () => { if (inflight.get(k) === run) inflight.delete(k); };
    run.then(done, done);
    return run;
  }
  async function cached(key, ttlMs, loader, { force = false, refresh = false, maxAge = 0 } = {}) {
    const k = cacheKey(key);
    if (!force && !refresh) {
      const hit = memory.get(k);
      const old = (x) => maxAge > 0 && Date.now() - (x.at || 0) > maxAge;
      const fresh = hit && !hit.kept && (!hit.until || hit.until > Date.now()) && !old(hit);
      if (fresh) return hit.value;
      if (hit && !old(hit) && (hit.kept || lapsed(key, hit))) return serveKept(k, key, ttlMs, loader, hit);
      if (inflight.has(k)) {
        if (drawing) promote(k); // a warm-up still queued that the screen being drawn wants: it goes up to the screen's place
        return inflight.get(k);
      }
    }
    return start(k, key, ttlMs, loader);
  }

  // ---- kept between page loads (2.99.33) ---------------------------------------------------------
  // The memo above starts empty on every page load, and a page loaded afresh used to wait on Canvas
  // for every list before it drew a thing. The store keeps the answers a screen opens with (store.js
  // says which, in what form, for which user and for how long) and seeds the memo with them as the
  // page loads, marked kept. A kept answer is handed to whoever asks for it, and Canvas is asked for
  // the key again in the same breath: the screen lands from what was kept, Canvas's answer replaces
  // it in the memo, and the app hears whether the two differed (revalidated()) so it can draw the
  // screen again in place. A caller that must not draw from an old answer (maxAge: a score) never
  // gets a kept one; a write forgets it like any other answer.
  //
  // The same goes for one of those lists whose lifetime in the memo has run out (lapsed): the
  // Dashboard opened again after five minutes in a course draws from the answer it had, and asks
  // again, rather than waiting on Canvas for every list it shows.
  let keptForm = null; // (key, value) → the form a kept answer takes (store.js)
  let onAnswer = null; // (key, value): an answer from Canvas landed in the memo (the store keeps the ones it keeps)
  let onDropped = null; // (which): a write invalidated these keys — what was kept of them is out of date too (the store drops it)
  let keptServed = 0; // kept answers handed out so far: the app compares the count before and after a draw
  const revalidations = new Set(); // one promise per key asked for again: whether Canvas's answer differed
  function keepWith({ form = null, answered = null, dropped = null } = {}) { keptForm = form; onAnswer = answered; onDropped = dropped; }
  /** The kept answers, into the memo: { key: { value, at } }, `at` when Canvas gave it. */
  function seed(entries) {
    for (const [key, e] of Object.entries(entries || {})) {
      const k = cacheKey(key);
      if (!e || e.value === undefined || memory.has(k) || inflight.has(k)) continue;
      memory.set(k, { value: e.value, at: Number(e.at) || 0, until: 1, kept: true }); // (until 1: never fresh, and the first to go under the cap)
    }
  }
  /** An answer whose lifetime has run out, of a kind that is kept: handed out as a kept one would be (its form, to compare with what comes next). */
  function lapsed(key, hit) {
    if (!keptForm || (hit.until && hit.until > Date.now()) || !hit.until) return false;
    try {
      const form = keptForm(key, hit.value);
      if (form === undefined) return false;
      hit.kept = true;
      hit.was = JSON.stringify(form);
      return true;
    } catch { return false; }
  }
  const asking = new Map(); // memo key → the promise of whether Canvas's answer differs from the one handed out
  function serveKept(k, key, ttlMs, loader, hit) {
    keptServed += 1;
    if (!asking.has(k)) {
      const was = hit.was ?? JSON.stringify(hit.value);
      const differs = (inflight.get(k) || start(k, key, ttlMs, loader)).then(
        (value) => { try { return JSON.stringify(keptForm ? keptForm(key, value) : value) !== was; } catch { return true; } },
        () => { if (memory.get(k) === hit) memory.delete(k); return false; }, // (Canvas did not answer: the next caller asks it, as before; what is drawn stays)
      );
      asking.set(k, differs);
      revalidations.add(differs);
      differs.finally(() => { revalidations.delete(differs); if (asking.get(k) === differs) asking.delete(k); });
    }
    return hit.value;
  }
  /** Whether any kept answer handed out so far turned out to differ from Canvas's, once every one asked for again has answered. */
  const revalidated = () => Promise.all([...revalidations]).then((r) => r.some(Boolean));
  const keptCount = () => keptServed;
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
    try { onDropped?.((k) => k === key); } catch { /* (kept or not, the next draw asks Canvas) */ }
  }
  /** Forget every memoised (or still loading) key that starts with the prefix (after a write). */
  async function invalidatePrefix(prefix) {
    const p = cacheKey(prefix);
    for (const k of new Set([...memory.keys(), ...inflight.keys()])) if (k.startsWith(p)) forget(k);
    try { onDropped?.((k) => k.startsWith(prefix)); } catch { /* (kept or not, the next draw asks Canvas) */ }
  }
  /** Forget everything (a page brought back from the back/forward cache, a re-tap of the current
   *  tab): the next draw asks Canvas afresh, exactly as a fresh page load does. */
  function clearAll() {
    for (const k of new Set([...memory.keys(), ...inflight.keys()])) forget(k);
  }


  BCV.canvas = {
    get, post, put, del, upload, cached, ready, invalidate, invalidatePrefix, clearAll, navigated, settled, tune, onSessionLost, sessionOk, checkSession, csrfToken, CanvasError,
    warming, budget: () => budget, keepWith, seed, revalidated, keptCount, // (2.99.33: reading ahead, and what is kept between page loads)
    /** The session has ended (lib/d2l-api.js says so for Brightspace, whose answers do not come through here). */
    signedOut: loseSession,
  };
})();
