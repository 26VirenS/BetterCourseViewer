/* Simpl Courses — shared settings module.
 * Classic script (no modules) so it can be loaded in the background
 * (service worker or event page), content scripts, popup and options.
 * Attaches to `self.BCV`. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const api = (BCV.api = BCV.api || (typeof browser !== 'undefined' ? browser : chrome));
  // The scripts' own version, stamped: content/app/app.js compares it with the stylesheet's
  // (--bcv-version) and the manifest's, because Safari can run one version's script with
  // another's stylesheet after the Mac app has updated under it. Bumped with every release.
  self.BCV_VERSION = '2.98.13';

  const DEFAULTS = {
    version: 3, // (3: Away Refresh off unless turned on — settings written before carry it on, and the one-time step in getSettings turns it off for everyone)
    search: {
      wikipedia: true,           // Search everything: Wikipedia's articles among the results (the switch in the box)
    },
    appearance: {
      skin: true,                 // the redesigned interface; off = stock Canvas
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
  };
})();
