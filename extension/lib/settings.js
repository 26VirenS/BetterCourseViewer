/* Simpl Courses — shared settings module.
 * Classic script (no modules) so it can be loaded in the background
 * (service worker or event page), content scripts, popup and options.
 * Attaches to `self.BCV`. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const api = (BCV.api = BCV.api || (typeof browser !== 'undefined' ? browser : chrome));

  const DEFAULTS = {
    version: 2,
    appearance: {
      skin: true,                 // the redesigned interface; off = stock Canvas
      darkMode: 'system',         // 'off' | 'on' | 'system'
      siteName: '',               // shown in the sidebar brand row; blank = derived from the host
      logoUrl: '',                // sidebar tile image; blank = the school's own mark from Canvas's theme
      sideCourses: 'always',      // where the favourite courses live: 'always' on the sidebar, or 'hover' off the Courses row
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

  async function getSettings() {
    try {
      const stored = await api.storage.local.get(STORAGE_KEY);
      return deepMerge(clone(DEFAULTS), stored[STORAGE_KEY] || {});
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
