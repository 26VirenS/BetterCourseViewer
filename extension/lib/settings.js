/* Simpl Courses — shared settings module.
 * Classic script (no modules) so it can be loaded in the background
 * (service worker or event page), content scripts, popup and options.
 * Attaches to `self.BCV`. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  BCV.api = BCV.api || (typeof browser !== 'undefined' ? browser : chrome);

  // ---- the lifeline (2.98.22) --------------------------------------------------------------------
  // When the extension updates, the scripts already running in an open Canvas tab are cut off from
  // it: Chrome makes every call to the extension throw ("Extension context invalidated"), Safari
  // simply never answers — and a screen waiting on storage or on a module to load (Tools, the
  // setup, a quiz) waited for ever, a skeleton that never filled. In a page, every storage call
  // and every message to the background now goes through here: one that throws that way, or that
  // is still unanswered after a few seconds while the background does not answer a ping either,
  // marks the page cut off. From then on calls fail at once instead of hanging, and whoever
  // listens (content/app/app.js) loads the page afresh, which runs the new version. Only in a
  // Canvas page: the background, the popup and the settings page are replaced with the update.
  const inPage = typeof location !== 'undefined' && /^https?:$/.test(location.protocol) && !self.BCVBridge?.native;
  if (inPage && !BCV.life && BCV.api?.runtime && BCV.api?.storage?.local) {
    const raw = BCV.api;
    const SLOW_MS = 3000; // a call unanswered this long has the background pinged
    const PING_MS = 5000; // and a ping unanswered this long means the page is cut off (a background page asleep wakes well inside it)
    const life = (BCV.life = { orphaned: false });
    const listeners = [];
    const GONE = 'Simpl Courses was updated while this page was open';
    const goneErr = () => Object.assign(new Error(GONE), { kind: 'LOAD', orphaned: true });
    const idGone = () => { try { return !raw.runtime?.id; } catch { return true; } };
    const deadErr = (e) => /context invalidated|extension context|invalid call to runtime|no such extension/i.test(String(e?.message || e || ''));
    function orphan() {
      if (life.orphaned) return;
      life.orphaned = true;
      try { self.BCV?.errors?.failed?.('LOAD'); } catch { /* no codes here */ } // (lib/errors.js: an error shown now reads SC-…-LOAD)
      for (const fn of listeners.splice(0)) { try { fn(); } catch { /* the others still hear */ } }
    }
    /** Called once the page is found cut off (at once when it already is). */
    life.onOrphaned = (fn) => { if (life.orphaned) setTimeout(fn, 0); else listeners.push(fn); };
    let probing = null;
    /** Whether the extension still answers this page: a ping to the background. False marks it cut off. */
    life.probe = () => {
      if (life.orphaned) return Promise.resolve(false);
      if (idGone()) { orphan(); return Promise.resolve(false); }
      if (probing) return probing;
      probing = new Promise((resolve) => {
        let done = false;
        const end = (ok) => { if (done) return; done = true; clearTimeout(t); probing = null; if (!ok) orphan(); resolve(ok); };
        const t = setTimeout(() => end(false), PING_MS);
        try {
          Promise.resolve(raw.runtime.sendMessage({ type: 'ping' })).then(() => end(true), (e) => end(!deadErr(e) && !idGone())); // (a background that answers with an error is still there)
        } catch (e) { end(!deadErr(e) && !idGone()); }
      });
      return probing;
    };
    /** A call to the extension that cannot hang the page: it answers, fails, or is found cut off. */
    const guard = (owner, name) => (...args) => {
      if (life.orphaned) return Promise.reject(goneErr());
      let p;
      try { p = Promise.resolve(owner()[name](...args)); } catch (e) {
        if (deadErr(e) || idGone()) { orphan(); return Promise.reject(goneErr()); }
        return Promise.reject(e);
      }
      return new Promise((resolve, reject) => {
        let settled = false;
        const settle = (fn, v) => { if (settled) return; settled = true; clearTimeout(slow); fn(v); };
        const slow = setTimeout(() => { life.probe().then((ok) => { if (!ok) settle(reject, goneErr()); }); }, SLOW_MS); // (alive, it keeps waiting: a big module can take a while)
        p.then((v) => settle(resolve, v), (e) => {
          if (deadErr(e) || idGone()) { orphan(); settle(reject, goneErr()); } else settle(reject, e);
        });
      });
    };
    const bindOf = (t, k) => { const v = t[k]; return typeof v === 'function' ? v.bind(t) : v; };
    // (the proxy stands on an empty object of its own, so the browser's own objects — whose
    // properties a proxy over them would have to report unchanged — are only ever read, never wrapped)
    const wrap = (target, own) => new Proxy({}, {
      get: (_, k) => (Object.prototype.hasOwnProperty.call(own, k) ? own[k] : bindOf(target, k)),
      has: (_, k) => k in own || k in target,
      set: (_, k, v) => { target[k] = v; return true; }, // (a test that swaps a call does so on the real object; the guard still stands in front of it)
    });
    const local = wrap(raw.storage.local, {
      get: guard(() => raw.storage.local, 'get'),
      set: guard(() => raw.storage.local, 'set'),
      remove: guard(() => raw.storage.local, 'remove'),
      clear: guard(() => raw.storage.local, 'clear'),
    });
    const storage = wrap(raw.storage, { local });
    const runtime = wrap(raw.runtime, { sendMessage: guard(() => raw.runtime, 'sendMessage') });
    BCV.api = wrap(raw, { runtime, storage });
    life.raw = raw; // (the tests reach past the guard with this)
  }
  const api = BCV.api;
  // The scripts' own version, stamped: content/app/app.js compares it with the stylesheet's
  // (--bcv-version) and the manifest's, because Safari can run one version's script with
  // another's stylesheet after the Mac app has updated under it. Bumped with every release.
  self.BCV_VERSION = '2.98.31';

  const DEFAULTS = {
    version: 3, // (3: Away Refresh off unless turned on — settings written before carry it on, and the one-time step in getSettings turns it off for everyone)
    search: {
      wikipedia: true,           // Search everything: Wikipedia's articles among the results (the switch in the box)
    },
    appearance: {
      skin: true,                 // the redesigned interface; off = stock Canvas
      offUntil: 0,                // turned off for a while (the switch's red list, 2.98.18): the time (ms) it comes back on by itself; 0 = until turned on again. Read through lookOn(), written through lookPatch()
      darkMode: 'system',         // 'off' | 'on' | 'system'
      siteName: '',               // shown in the sidebar brand row; blank = derived from the host
      logoUrl: '',                // sidebar tile image; blank = the school's own mark from Canvas's theme
      sideCourses: 'always',      // where the favourite courses live: 'always' on the sidebar, or 'hover' off the Courses row
      awayRefresh: false,         // on: a page left three minutes reloads itself when you come back (content/app/app.js); off unless turned on with the switch under General (a hold on the pill turns it off again)
      dashboard: { cards: true, list: true, activity: true }, // which of the Dashboard's views are offered; at least one stays on
      theme: { accent: '' },   // the colour of the student's own (lib/theme.js); blank = the interface's blue. The photos are in storage.local under theme:images
    },
    domains: [],                  // extra Canvas origins, e.g. "https://canvas.myschool.edu"
  };

  const STORAGE_KEY = 'settings';

  function isObject(v) {
    return v && typeof v === 'object' && !Array.isArray(v);
  }

  function deepMerge(base, patch) {
    const out = Array.isArray(base) ? base.slice() : { ...base };
    if (!isObject(patch)) return patch === undefined ? out : patch;
    for (const [k, v] of Object.entries(patch)) {
      if (isObject(v) && isObject(out[k])) out[k] = deepMerge(out[k], v);
      else if (v !== undefined) out[k] = Array.isArray(v) ? v.slice() : v;
    }
    return out;
  }

  function clone(v) {
    return JSON.parse(JSON.stringify(v));
  }

  /** Settings written by an earlier version, brought up to this one — once, and written back so it
   *  is not done again. Version 3 (2.98.13): Away Refresh is off unless turned on; every write
   *  before then kept it on (the default was on), so it is turned off for everyone here, and the
   *  switch under General is the way on. */
  async function migrate(stored) {
    if (!isObject(stored) || !Object.keys(stored).length || (Number(stored.version) || 0) >= 3) return stored;
    const next = deepMerge(stored, { version: 3, appearance: { awayRefresh: false } });
    try { await api.storage.local.set({ [STORAGE_KEY]: next }); } catch { /* read as brought up to date all the same */ }
    return next;
  }
  async function getSettings() {
    try {
      const raw = await api.storage.local.get(STORAGE_KEY);
      const stored = await migrate(raw[STORAGE_KEY] || {});
      return deepMerge(clone(DEFAULTS), stored || {});
    } catch (e) {
      return clone(DEFAULTS);
    }
  }

  async function updateSettings(patch) {
    const current = await getSettings();
    const next = deepMerge(current, patch);
    await api.storage.local.set({ [STORAGE_KEY]: next });
    return next;
  }

  async function replaceSettings(next) {
    const merged = deepMerge(clone(DEFAULTS), next || {});
    await api.storage.local.set({ [STORAGE_KEY]: merged });
    return merged;
  }

  function onSettingsChange(callback) {
    const handler = (changes, area) => {
      if (area !== 'local' || !changes[STORAGE_KEY]) return;
      const next = deepMerge(clone(DEFAULTS), changes[STORAGE_KEY].newValue || {});
      callback(next);
    };
    api.storage.onChanged.addListener(handler);
    return () => api.storage.onChanged.removeListener(handler);
  }

  /** Effective appearance: is dark on, given the system preference. */
  function isDark(settings, systemDark) {
    const mode = settings?.appearance?.darkMode || 'system';
    return mode === 'on' || (mode === 'system' && !!systemDark);
  }

  /** Is Simpl on, by the saved settings? On unless turned off — and a turn-off for a while (the
   *  switch's red list: 30 minutes, an hour, four hours, a day) is over once its time has come,
   *  whether or not anything has written that back yet. Every reader of the saved look asks this. */
  function lookOn(settings, now = Date.now()) {
    const a = settings?.appearance || {};
    if (a.skin !== false) return true;
    const until = Number(a.offUntil) || 0;
    return until > 0 && now >= until;
  }
  /** The patch that saves the look: on, or off until a time (ms) — 0 for until turned on again.
   *  Every writer goes through it, so an old time is never left behind to turn Simpl on later. */
  function lookPatch(on, until = 0) {
    return { appearance: { skin: !!on, offUntil: on ? 0 : Math.max(0, Number(until) || 0) } };
  }

  BCV.settings = {
    DEFAULTS,
    STORAGE_KEY,
    deepMerge,
    clone,
    get: getSettings,
    update: updateSettings,
    replace: replaceSettings,
    onChange: onSettingsChange,
    isDark,
    lookOn,
    lookPatch,
  };
})();
