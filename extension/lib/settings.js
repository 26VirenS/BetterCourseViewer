/* BetterCourseViewer — shared settings module.
 * Classic script (no modules) so it can be loaded in the background
 * (service worker or event page), content scripts, popup and options.
 * Attaches to `self.BCV`. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const api = (BCV.api = BCV.api || (typeof browser !== 'undefined' ? browser : chrome));

  const DEFAULTS = {
    version: 1,
    appearance: {
      skin: true,                 // redesigned interface (rounded, minimal)
      darkMode: 'off',            // 'off' | 'on' | 'system'
      theme: 'default',           // preset id or 'custom'
      custom: {
        accent: '#4f46e5',
        nav: '#1f2937',
        navText: '#f9fafb',
        link: '#4338ca',
      },
      font: 'system',             // 'system' | 'rounded' | 'serif' | 'mono'
      density: 'comfortable',     // 'comfortable' | 'compact'
      minimal: false,
      contentWidth: 'default',    // 'default' | 'narrow' | 'wide'
    },
    clean: {
      hideRightSidebar: true,
      hideFooter: true,
      hideHelpNav: false,
      hideToolNav: false,
      hideHistoryNav: false,
      hideGroupsNav: false,
      compactCards: true,
      hideCardImages: false,
      hideCardActions: true,
      hideCardTerm: true,
      hideBreadcrumbs: false,
      hideAnnouncementsBanner: false,
      cardColumns: 'auto',        // 'auto' | '3' | '4' | '5'
    },
    dueDates: {
      highlight: true,
      dashboardPanel: true,
      reminders: true,
      reminderWindowHours: 24,
      lookaheadDays: 14,
      badge: true,
    },
    todo: {
      enabled: true,
      showCanvasItems: true,
      hideCompleted: true,
      filter: 'all',              // 'all' | 'assignment' | 'quiz' | 'discussion' | 'other'
    },
    keyboard: {
      enabled: true,
      palette: true,
    },
    embeds: {
      openInNewTabButton: true,
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
      sidebarWidth: 400,
    },
    domains: [],                  // extra Canvas origins, e.g. "https://canvas.myschool.edu"
  };

  // Theme presets. `null` means "leave Canvas's own branding alone".
  const THEMES = {
    default:  { label: 'Canvas default', accent: null, nav: null, navText: null, link: null },
    indigo:   { label: 'Indigo',   accent: '#4f46e5', nav: '#1e1b4b', navText: '#eef2ff', link: '#4338ca' },
    midnight: { label: 'Midnight', accent: '#3b82f6', nav: '#0f172a', navText: '#e2e8f0', link: '#2563eb' },
    forest:   { label: 'Forest',   accent: '#15803d', nav: '#14352a', navText: '#ecfdf5', link: '#166534' },
    rose:     { label: 'Rose',     accent: '#be185d', nav: '#3f0d2a', navText: '#fdf2f8', link: '#9d174d' },
    sunset:   { label: 'Sunset',   accent: '#ea580c', nav: '#2b1a12', navText: '#fff7ed', link: '#c2410c' },
    teal:     { label: 'Teal',     accent: '#0d9488', nav: '#0f2f2e', navText: '#f0fdfa', link: '#0f766e' },
    mono:     { label: 'Mono',     accent: '#111827', nav: '#111827', navText: '#f9fafb', link: '#111827' },
    custom:   { label: 'Custom',   accent: null, nav: null, navText: null, link: null },
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

  /** Resolve the effective theme colours (null = keep Canvas defaults). */
  function effectiveTheme(appearance) {
    const preset = THEMES[appearance.theme] || THEMES.default;
    if (appearance.theme === 'custom') return { ...appearance.custom };
    return { accent: preset.accent, nav: preset.nav, navText: preset.navText, link: preset.link };
  }

  /** html-element class list derived from settings (used by early.js and theme.js). */
  function classesFor(settings, systemDark) {
    const a = settings.appearance;
    const c = settings.clean;
    const classes = [];
    const dark = a.darkMode === 'on' || (a.darkMode === 'system' && systemDark);
    if (dark) classes.push('bcv-dark');
    if (a.skin !== false) classes.push('bcv-skin');
    if (a.minimal) classes.push('bcv-minimal');
    if (a.density === 'compact') classes.push('bcv-compact');
    if (a.font && a.font !== 'system') classes.push('bcv-font-' + a.font);
    if (a.contentWidth && a.contentWidth !== 'default') classes.push('bcv-width-' + a.contentWidth);
    if (c.hideRightSidebar) classes.push('bcv-hide-sidebar');
    if (c.hideFooter) classes.push('bcv-hide-footer');
    if (c.hideHelpNav) classes.push('bcv-hide-help');
    if (c.hideToolNav) classes.push('bcv-hide-tools');
    if (c.hideHistoryNav) classes.push('bcv-hide-history');
    if (c.hideGroupsNav) classes.push('bcv-hide-groups');
    if (c.compactCards) classes.push('bcv-compact-cards');
    if (c.hideCardImages) classes.push('bcv-hide-card-images');
    if (c.hideCardActions) classes.push('bcv-hide-card-actions');
    if (c.hideCardTerm) classes.push('bcv-hide-card-term');
    if (c.hideBreadcrumbs) classes.push('bcv-hide-breadcrumbs');
    if (c.hideAnnouncementsBanner) classes.push('bcv-hide-announcements');
    if (c.cardColumns && c.cardColumns !== 'auto') classes.push('bcv-cards-' + c.cardColumns);
    return classes;
  }

  BCV.settings = {
    DEFAULTS,
    THEMES,
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
    effectiveTheme,
    classesFor,
  };
})();
