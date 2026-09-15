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
    },
    quizzes: {
      // A quiz Canvas locks — each question sealed the moment you leave it, no going back — cannot be
      // retried if anything here goes wrong, so it is handed to Canvas's own page by default. Turning
      // this on takes that guard off; the disclaimer on the switch says what that means.
      lockedHere: false,
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

  /** May a quiz Canvas locks (no going back) be taken in this interface? Off unless it was asked for. */
  function quizzesHere(settings) {
    return !!settings?.quizzes?.lockedHere;
  }

  /** The words the switch has to be accepted with before a locked quiz may be taken here. */
  const LOCKED_QUIZ_DISCLAIMER = 'A quiz with "lock questions after answering" on cannot be gone back to: '
    + 'once you leave a question Canvas seals your answer, and an attempt that goes wrong cannot be taken again.\n\n'
    + 'Simpl Courses draws its own quiz screen over Canvas’s API. It is not made by your school or by Instructure, '
    + 'and it is provided as is: nobody behind it is liable for a lost answer, a lost attempt or a lost grade, '
    + 'however caused.\n\n'
    + 'Turn this on and locked quizzes will be taken here instead of on Canvas’s own page.';

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
    quizzesHere,
    LOCKED_QUIZ_DISCLAIMER,
  };
})();
