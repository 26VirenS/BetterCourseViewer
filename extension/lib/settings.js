/* BetterCourseViewer — shared settings module.
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
    },
    smart: {
      enabled: true,
      claudeKey: '',
      openaiKey: '',
      preferred: 'claude',        // when both keys are set
      claudeModel: 'claude-opus-5',
      openaiModel: 'gpt-5',
      depth: 'balanced',          // 'quick' | 'balanced' | 'thorough'
      includePageContext: true,
      persistChat: true,
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

  /** Which smart provider to use, given the configured keys. */
  function resolveProvider(smart) {
    const hasClaude = !!(smart.claudeKey && smart.claudeKey.trim());
    const hasOpenAI = !!(smart.openaiKey && smart.openaiKey.trim());
    if (hasClaude && hasOpenAI) return smart.preferred === 'openai' ? 'openai' : 'claude';
    if (hasClaude) return 'claude';
    if (hasOpenAI) return 'openai';
    return null;
  }

  function providerLabel(provider) {
    return provider === 'openai' ? 'ChatGPT' : provider === 'claude' ? 'Claude' : 'Not set up';
  }

  function modelFor(smart, provider) {
    return provider === 'openai'
      ? (smart.openaiModel || DEFAULTS.smart.openaiModel)
      : (smart.claudeModel || DEFAULTS.smart.claudeModel);
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
    resolveProvider,
    providerLabel,
    modelFor,
    isDark,
  };
})();
