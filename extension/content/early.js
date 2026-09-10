/* Runs at document_start: applies the html classes (dark mode, declutter,
 * minimal…) and cached theme CSS before Canvas paints, then reconciles
 * with extension storage. */
(function () {
  const BCV = self.BCV;
  const S = BCV.settings;
  const html = document.documentElement;
  const CLASS_KEY = 'bcv:classes';
  const CSS_KEY = 'bcv:theme-css';

  const systemDark = () => !!window.matchMedia?.('(prefers-color-scheme: dark)').matches;

  function pageClass() {
    const p = location.pathname.replace(/\/+$/, '') || '/';
    if (p === '/' || p.startsWith('/dashboard')) return 'bcv-page-dashboard';
    if (/^\/courses\/\d+$/.test(p)) return 'bcv-page-course-home';
    if (/^\/courses\/\d+\/assignments\/\d+/.test(p)) return 'bcv-page-assignment';
    if (/^\/courses\/\d+\/modules/.test(p)) return 'bcv-page-modules';
    if (/^\/courses\/\d+\/grades/.test(p)) return 'bcv-page-grades';
    if (p === '/courses') return 'bcv-page-courses';
    return 'bcv-page-other';
  }

  // Classes owned by features at runtime, not derived from settings.
  const PRESERVE = new Set(['bcv-has-greeting', 'bcv-panel-open']);

  function applyClasses(classes) {
    for (const c of Array.from(html.classList)) {
      if (c.startsWith('bcv-') && !c.startsWith('bcv-page-') && !PRESERVE.has(c)) html.classList.remove(c);
    }
    html.classList.add(...classes);
    if (!Array.from(html.classList).some((c) => c.startsWith('bcv-page-'))) html.classList.add(pageClass());
  }

  function styleEl() {
    let el = document.getElementById('bcv-theme');
    if (!el) {
      el = document.createElement('style');
      el.id = 'bcv-theme';
      (document.head || html).append(el);
    }
    return el;
  }

  // 1. Instant: cached values from the page origin's localStorage.
  try {
    const cached = localStorage.getItem(CLASS_KEY);
    if (cached) applyClasses(JSON.parse(cached));
    const css = localStorage.getItem(CSS_KEY);
    if (css) styleEl().textContent = css;
  } catch {
    /* localStorage may be unavailable */
  }
  if (!Array.from(html.classList).some((c) => c.startsWith('bcv-page-'))) html.classList.add(pageClass());

  // 2. Authoritative: extension storage.
  let current = null;
  async function sync(settings) {
    current = settings || (await S.get());
    const dark = current.appearance.darkMode === 'on' || (current.appearance.darkMode === 'system' && systemDark());
    const classes = S.classesFor(current, systemDark());
    applyClasses(classes);
    let css = '';
    try {
      css = BCV.themeCss.build(current, dark);
      styleEl().textContent = css;
    } catch {
      /* ignore */
    }
    try {
      localStorage.setItem(CLASS_KEY, JSON.stringify(classes));
      localStorage.setItem(CSS_KEY, css);
    } catch {
      /* ignore */
    }
    return { classes, dark };
  }
  sync();
  S.onChange((s) => sync(s));
  try {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => sync(current));
  } catch {
    /* ignore */
  }

  BCV.early = { sync, systemDark, isDark: () => html.classList.contains('bcv-dark') };
})();
