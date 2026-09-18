/* The page after install (Safari and Chrome): the same glass card as the guided setup, with the
 * three things to do — each one drawn, not only described. The steps themselves run over the
 * Canvas page: open Canvas, press the toolbar button, press Set up (see content/app/setup.js). */
(async function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const S = BCV.settings;
  const { h } = BCV.utils;

  // ---- appearance: the extension's choice, else the system's ------------------------------------
  const mq = window.matchMedia?.('(prefers-color-scheme: dark)');
  let settings = await S.get();
  const paint = () => {
    const mode = settings.appearance?.darkMode || 'system';
    const dark = mode === 'on' || (mode === 'system' && !!mq?.matches);
    document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
  };
  paint();
  mq?.addEventListener?.('change', paint);
  S.onChange((s) => { settings = s; paint(); });

  const body = document.getElementById('body');
  const foot = document.getElementById('foot');
  const safari = /apple/i.test(navigator.vendor || '') && !/chrome|crios|edg/i.test(navigator.userAgent);
  const svg = (d, { size = 14, width = 1.9, cls = '' } = {}) => h('span', { class: cls, 'aria-hidden': 'true', html: `<svg viewBox="0 0 24 24" width="${size}" height="${size}" fill="none" stroke="currentColor" stroke-width="${width}" stroke-linecap="round" stroke-linejoin="round"><path d="${d}"/></svg>` });
  const PUZZLE = 'M10 4a2 2 0 114 0v1h3a1 1 0 011 1v3h1a2 2 0 110 4h-1v3a1 1 0 01-1 1h-3v-1a2 2 0 10-4 0v1H7a1 1 0 01-1-1v-3H5a2 2 0 110-4h1V6a1 1 0 011-1h3z';
  const LOCK = 'M7 11V8a5 5 0 0110 0v3M6 11h12v9H6z';
  const PIN = 'M12 17v4M8 3h8l-1 6 3 3v2H6v-2l3-3z';
  const STAR = 'M12 3l2.7 5.6 6.1.9-4.4 4.3 1 6.1L12 17l-5.4 2.9 1-6.1L3.2 9.5l6.1-.9z';
  const SEARCH = 'M11 4a7 7 0 100 14 7 7 0 000-14zM20 20l-3.5-3.5';
  const DOTS = 'M5 12h.01M12 12h.01M19 12h.01';
  const SHEET = '<svg viewBox="0 0 120 120" width="100%" height="100%"><rect x="16" y="18" width="53" height="84" rx="14" fill="rgba(255,255,255,.35)"/><path d="M28 30 H69 A30 30 0 0 1 69 90 H28" fill="none" stroke="#fff" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/><path d="M35 42 H69 A18 18 0 0 1 69 78 H35" fill="none" stroke="rgba(255,255,255,.72)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/><path d="M42 54 H69 A6 6 0 0 1 69 66 H42" fill="none" stroke="rgba(255,255,255,.46)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/></svg>';
  const markXs = () => h('span', { class: 'mark mark--xs', 'aria-hidden': 'true', html: SHEET });
  const mark = h('span', { class: 'mark mark--lg', 'aria-hidden': 'true' });
  mark.innerHTML = '<svg viewBox="0 0 120 120" width="76" height="76"><defs><linearGradient id="sheetLg" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#fff" stop-opacity=".9"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient></defs><rect class="sheet" x="16" y="18" width="53" height="84" rx="14" fill="url(#sheetLg)"/><path class="arc arc--1" style="--len:178" d="M28 30 H69 A30 30 0 0 1 69 90 H28" fill="none" stroke="#fff" stroke-width="8" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="178"/><path class="arc arc--2" style="--len:126" d="M35 42 H69 A18 18 0 0 1 69 78 H35" fill="none" stroke="rgba(255,255,255,.72)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="126"/><path class="arc arc--3" style="--len:74" d="M42 54 H69 A6 6 0 0 1 69 66 H42" fill="none" stroke="rgba(255,255,255,.46)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="74"/></svg>';

  // ---- the three steps, each with a small picture of the thing to find --------------------------
  // 1. the address bar, on Canvas
  const picCanvas = h('div', { class: 'pic', 'aria-hidden': 'true' }, [
    h('span', { class: 'pic__bar' }, [svg(LOCK, { size: 12, width: 2 }), h('span', { text: 'yourschool.instructure.com' })]),
  ]);
  // 2. the toolbar: Chrome's puzzle piece and the menu behind it, or Safari's own button
  const picToolbar = safari
    ? h('div', { class: 'pic', 'aria-hidden': 'true' }, [
      h('div', { class: 'pic__tool' }, [
        h('span', { class: 'pic__ico' }, svg(SEARCH, { size: 13 })),
        h('span', { class: 'pic__ico' }, svg(DOTS, { size: 13, width: 2.6 })),
        h('span', { class: 'pic__ico pic__ico--hot pic__ico--mark' }, markXs()),
        h('span', { class: 'pic__hint', text: '← this one' }),
      ]),
    ])
    : h('div', { class: 'pic', 'aria-hidden': 'true' }, [
      h('div', { class: 'pic__tool' }, [
        h('span', { class: 'pic__ico' }, svg(STAR, { size: 13 })),
        h('span', { class: 'pic__ico pic__ico--hot' }, svg(PUZZLE, { size: 15 })),
        h('span', { class: 'pic__hint', text: '← the puzzle piece' }),
      ]),
      h('div', { class: 'pic__menu' }, [markXs(), h('span', { text: 'Simpl Courses' }), h('span', { class: 'pic__pin' }, svg(PIN, { size: 13 }))]),
    ]);
  // 3. the popup, with its one button
  const picPopup = h('div', { class: 'pic', 'aria-hidden': 'true' }, [
    h('div', { class: 'pic__popup' }, [
      h('div', { class: 'pic__popuphead' }, [markXs(), h('span', { text: 'Simpl Courses' })]),
      h('span', { class: 'pic__btn', text: 'Set up' }),
    ]),
  ]);

  const steps = [
    ['Open your Canvas', h('span', {}, ['Sign in to your school\'s Canvas in this browser.']), picCanvas],
    safari
      ? ['Press the Simpl Courses button', h('span', {}, ['It\'s in the toolbar. If Safari asks, choose ', h('b', { text: 'Always Allow on This Website' }), '.']), picToolbar]
      : ['Open Simpl Courses', h('span', {}, ['Click the puzzle piece, then ', h('b', { text: 'Simpl Courses' }), '. Pin it to keep it in the toolbar.']), picToolbar],
    ['Press Set up', h('span', {}, [safari ? 'Setup runs on the page.' : 'Chrome asks once to allow the site. Then setup runs on the page.']), picPopup],
  ].map(([t, s, pic], i) => h('div', { class: 'how__step how__step--pic' }, [
    h('span', { class: 'how__n', text: String(i + 1) }),
    h('div', { class: 'how__body' }, [h('span', { class: 'how__t', text: t }), h('span', { class: 'how__s' }, [s]), pic]),
  ]));
  steps.forEach((el, i) => { el.style.animationDelay = `${220 + i * 90}ms`; });

  body.append(
    h('div', { class: 'welcome' }, [
      mark,
      h('h1', { class: 'h1', text: 'Simpl Courses is installed' }),
      h('p', { class: 'lead', text: 'Three quick steps.' }),
    ]),
    h('div', { class: 'how' }, steps),
  );
  foot.append(
    h('span', { class: 'foot__spacer' }),
    h('button', { type: 'button', class: 'btn', id: 'next', text: 'Got it', onclick: () => window.close() }),
  );
  await api.storage.local.set({ 'setup:offered': true }).catch(() => {});
})();
