/* The page after install (Safari and Chrome): the same glass card as the guided setup, with the
 * three things to do. The steps themselves run over the Canvas page: open Canvas, press the
 * toolbar button, press Set up (see content/app/setup.js). */
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
  const PUZZLE = '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10 4a2 2 0 114 0v1h3a1 1 0 011 1v3h1a2 2 0 110 4h-1v3a1 1 0 01-1 1h-3v-1a2 2 0 10-4 0v1H7a1 1 0 01-1-1v-3H5a2 2 0 110-4h1V6a1 1 0 011-1h3z"/></svg>';
  const mark = h('span', { class: 'mark mark--lg', 'aria-hidden': 'true' });
  mark.innerHTML = '<svg viewBox="0 0 120 120" width="76" height="76"><defs><linearGradient id="sheetLg" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#fff" stop-opacity=".9"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient></defs><rect class="sheet" x="16" y="18" width="53" height="84" rx="14" fill="url(#sheetLg)"/><path class="arc arc--1" style="--len:178" d="M28 30 H69 A30 30 0 0 1 69 90 H28" fill="none" stroke="#fff" stroke-width="8" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="178"/><path class="arc arc--2" style="--len:126" d="M35 42 H69 A18 18 0 0 1 69 78 H35" fill="none" stroke="rgba(255,255,255,.72)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="126"/><path class="arc arc--3" style="--len:74" d="M42 54 H69 A6 6 0 0 1 69 66 H42" fill="none" stroke="rgba(255,255,255,.46)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="74"/></svg>';

  const steps = [
    ['Open your Canvas', h('span', {}, ['Go to your school\'s Canvas and open your courses page, for example ', h('b', { text: 'school.instructure.com' }), '.'])],
    safari
      ? ['Press the Simpl Courses button', h('span', {}, ['It is the ', h('i', { class: 'how__icon', 'aria-hidden': 'true' }), ' button in the toolbar. If Safari asks, allow Simpl Courses on the site.'])]
      : ['Press the puzzle piece, then Simpl Courses', h('span', {}, ['Click the ', h('i', { class: 'how__puzzle', 'aria-hidden': 'true', html: PUZZLE }), ' puzzle piece at the right of the toolbar and choose ', h('b', { text: 'Simpl Courses' }), ' ', h('i', { class: 'how__icon', 'aria-hidden': 'true' }), '. Press the pin next to it and the button stays in the toolbar.'])],
    ['Press Set up', h('span', {}, ['Simpl Courses asks to run on that site, then walks through your courses, grades and the smart panel, and ends with a tour.'])],
  ].map(([t, s], i) => h('div', { class: 'how__step' }, [h('span', { class: 'how__n', text: String(i + 1) }), h('div', {}, [h('span', { class: 'how__t', text: t }), h('span', { class: 'how__s' }, [s])])]));
  steps.forEach((el, i) => { el.style.animationDelay = `${220 + i * 90}ms`; });

  body.append(
    h('div', { class: 'welcome' }, [
      mark,
      h('h1', { class: 'h1', text: 'Simpl Courses is installed' }),
      h('p', { class: 'lead', text: 'Set it up from your Canvas page. Three things to do.' }),
    ]),
    h('div', { class: 'how' }, steps),
  );
  foot.append(
    h('span', { class: 'foot__spacer' }),
    h('button', { type: 'button', class: 'btn', id: 'next', text: 'Got it', onclick: () => window.close() }),
  );
  await api.storage.local.set({ 'setup:offered': true }).catch(() => {});
})();
