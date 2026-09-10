/* Runs at document_start: decides before first paint whether the redesigned
 * interface is on and which appearance (light/dark) to use, so Canvas's own
 * chrome never flashes. The authoritative values come from extension
 * storage; a per-origin localStorage copy is applied instantly. */
(function () {
  // Canvas pages loaded inside a frame (a tool's file picker, a file preview,
  // an LTI return page) are someone else's UI: leave them exactly as they are.
  if (window.self !== window.top) return;
  const BCV = self.BCV;
  const S = BCV.settings;
  const html = document.documentElement;
  const CACHE_KEY = 'bcv:early';

  const systemDark = () => !!window.matchMedia?.('(prefers-color-scheme: dark)').matches;

  function apply({ skin, dark }) {
    html.classList.toggle('bcv-on', skin !== false);
    html.setAttribute('data-bcv-theme', dark ? 'dark' : 'light');
  }

  // 1. Instant: cached values from the page origin's localStorage.
  try {
    const cached = localStorage.getItem(CACHE_KEY);
    if (cached) apply(JSON.parse(cached));
    else apply({ skin: true, dark: systemDark() });
  } catch {
    apply({ skin: true, dark: systemDark() });
  }

  // 2. Authoritative: extension storage.
  let current = null;
  const listeners = new Set();
  async function sync(settings) {
    current = settings || (await S.get());
    const state = { skin: current.appearance.skin !== false, dark: S.isDark(current, systemDark()) };
    apply(state);
    try {
      localStorage.setItem(CACHE_KEY, JSON.stringify(state));
    } catch {
      /* ignore */
    }
    for (const fn of listeners) {
      try {
        fn(state, current);
      } catch {
        /* ignore */
      }
    }
    return state;
  }
  const ready = sync();
  S.onChange((s) => sync(s));
  // The navigation bar (mockup 8): a 2.5px sweep at the top while this page
  // loads; app.js hides it once the screen is drawn. It only shows with the look on.
  if (!document.getElementById('bcv-progress')) {
    const bar = document.createElement('div');
    bar.id = 'bcv-progress';
    bar.setAttribute('aria-hidden', 'true');
    bar.append(document.createElement('div'));
    bar.firstChild.className = 'bcv-progress__bar';
    html.append(bar);
  }
  // Safety net: if the interface never mounts (script error, blocked page),
  // give the page back to Canvas rather than leaving it blank.
  setTimeout(() => {
    if (html.classList.contains('bcv-on') && !document.getElementById('bcv-app')) html.classList.remove('bcv-on');
  }, 8000);
  try {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => sync(current));
  } catch {
    /* ignore */
  }

  BCV.early = {
    ready,
    sync,
    systemDark,
    settings: () => current,
    isDark: () => html.getAttribute('data-bcv-theme') === 'dark',
    isOn: () => html.classList.contains('bcv-on'),
    onChange: (fn) => {
      listeners.add(fn);
      return () => listeners.delete(fn);
    },
  };
})();
