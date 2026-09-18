/* The page after install (Safari and Chrome): black, the way the welcome on the page is — a splash,
 * then an arrow to where the button lives with what to press there, then where to go. Continue
 * comes in after a moment on the pointer screen (time to find the thing first), Got it on the last.
 * The setup itself runs over the Canvas page: open Canvas, press the toolbar button, press Set up
 * (see content/app/setup.js). */
(async function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const { h } = BCV.utils;
  const safari = /apple/i.test(navigator.vendor || '') && !/chrome|crios|edg/i.test(navigator.userAgent);
  const WAIT = 4000; // Continue comes in after this long: time to take the pointer in first
  const SPLASH = 1600; // the wordmark holds this long before the first screen
  const SHEET = '<svg viewBox="0 0 120 120" width="100%" height="100%"><rect x="16" y="18" width="53" height="84" rx="14" fill="rgba(255,255,255,.35)"/><path d="M28 30 H69 A30 30 0 0 1 69 90 H28" fill="none" stroke="#fff" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/><path d="M35 42 H69 A18 18 0 0 1 69 78 H35" fill="none" stroke="rgba(255,255,255,.72)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/><path d="M42 54 H69 A6 6 0 0 1 69 66 H42" fill="none" stroke="rgba(255,255,255,.46)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/></svg>';
  const SVG = 'http://www.w3.org/2000/svg';
  const svgEl = (tag, attrs = {}) => { const el = document.createElementNS(SVG, tag); for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, String(v)); return el; };

  const root = document.getElementById('splash');
  document.documentElement.classList.add('is-splash');
  let current = null;
  let splashTimer = 0; // the splash moving on by itself
  let revealTimer = 0; // a screen's button coming in
  const button = (text, onclick) => h('button', { type: 'button', class: 'splash__btn', text, hidden: true, onclick });
  /** The next screen in, the last one out. */
  function show(el) {
    clearTimeout(splashTimer);
    const old = current;
    if (old) { old.classList.add('is-leaving'); setTimeout(() => old.remove(), 300); }
    current = el;
    root.append(el);
  }
  const reveal = (btn, ms) => { clearTimeout(revealTimer); revealTimer = setTimeout(() => { btn.hidden = false; }, ms); };

  // ---- the arrow: from the words up to the thing to press --------------------------------------
  // Chrome keeps its extensions behind the puzzle piece in the toolbar, above the page: right of
  // the address bar, left of the profile picture and the ⋮ menu — about a hundred pixels in from
  // the window's right edge, never in the corner itself (that is the menu). The arrow rises to the
  // top of the page straight under it, so the head points up at the piece. Edge puts the piece a
  // little further in. Safari has no such piece: the button sits in the toolbar, so the arrow simply
  // points up at the bar. Drawn in the window's own pixels, and again when the window changes size.
  const PIECE = /edg\//i.test(navigator.userAgent) ? 150 : 104; // the piece's centre, in from the right
  function arrow(kind) {
    const svg = svgEl('svg', { class: 'splash__arrow', 'aria-hidden': 'true' });
    const line = svgEl('path', { class: 'splash__line' });
    const head = svgEl('path', { class: 'splash__head' });
    svg.append(line, head);
    const draw = () => {
      const W = window.innerWidth, H = window.innerHeight;
      svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
      const start = { x: W / 2, y: H * 0.5 - 150 };
      const end = kind === 'corner' ? { x: Math.max(W / 2 + 40, W - PIECE), y: 14 } : { x: W / 2, y: 28 };
      // a quadratic curve arrives along end − ctrl: the control point sits straight under the tip,
      // so the line leaves the words sideways and comes up vertically under the piece
      const ctrl = kind === 'corner' ? { x: end.x, y: start.y } : { x: W / 2, y: (start.y + end.y) / 2 };
      line.setAttribute('d', `M${start.x} ${start.y} Q${ctrl.x} ${ctrl.y} ${end.x} ${end.y}`);
      const a = Math.atan2(end.y - ctrl.y, end.x - ctrl.x); // the way the line arrives at its tip
      const L = 16;
      const p1 = { x: end.x - L * Math.cos(a - 0.5), y: end.y - L * Math.sin(a - 0.5) };
      const p2 = { x: end.x - L * Math.cos(a + 0.5), y: end.y - L * Math.sin(a + 0.5) };
      head.setAttribute('d', `M${p1.x.toFixed(1)} ${p1.y.toFixed(1)} L${end.x} ${end.y} L${p2.x.toFixed(1)} ${p2.y.toFixed(1)}`);
      const len = line.getTotalLength();
      line.style.strokeDasharray = String(len);
      line.style.strokeDashoffset = String(len);
    };
    requestAnimationFrame(draw);
    window.addEventListener('resize', () => { if (svg.isConnected) draw(); });
    return svg;
  }

  // ---- the three screens ---------------------------------------------------------------------
  function splash() {
    return h('div', { class: 'splash__stage splash__stage--word', dataset: { stage: 'splash' } }, [
      h('div', { class: 'splash__brand' }, [
        h('span', { class: 'mark mark--splash', 'aria-hidden': 'true', html: SHEET }),
        h('span', { class: 'splash__word', text: 'Simpl.' }),
      ]),
    ]);
  }
  function pin() {
    const btn = button('Continue', () => show(go()));
    const el = h('div', { class: `splash__stage splash__stage--pin${safari ? ' is-safari' : ''}`, dataset: { stage: 'pin' } }, [
      arrow(safari ? 'up' : 'corner'),
      h('div', { class: 'splash__text' }, [
        h('span', { class: 'splash__kicker', text: safari ? 'In the toolbar' : 'Up here' }),
        h('h1', { class: 'splash__title', text: safari ? 'Find the Simpl Courses button' : 'Press the puzzle piece' }),
        h('p', { class: 'splash__hint' }, safari
          ? ['It sits in the bar at the top. If Safari asks, choose ', h('b', { text: 'Always Allow on This Website' }), '.']
          : ['Then press the pin next to ', h('b', { text: 'Simpl Courses' }), ', so it stays in the toolbar.']),
        btn,
      ]),
    ]);
    reveal(btn, WAIT);
    return el;
  }
  function go() {
    const note = h('p', { class: 'splash__note', text: 'You can close this tab.', hidden: true });
    const btn = button('Got it', () => { try { window.close(); } catch { /* a tab the page did not open stays */ } setTimeout(() => { note.hidden = false; }, 400); });
    const el = h('div', { class: 'splash__stage splash__stage--go', dataset: { stage: 'go' } }, [
      h('div', { class: 'splash__text' }, [
        h('span', { class: 'splash__kicker', text: 'Then' }),
        h('h1', { class: 'splash__title', text: 'Open your Canvas' }),
        h('p', { class: 'splash__hint' }, ['Go to your school\'s Canvas page and press the ', h('b', { text: 'Simpl Courses' }), ' button. Setup runs there.']),
        btn,
        note,
      ]),
    ]);
    reveal(btn, 600);
    return el;
  }

  // Enter presses the button that is up; a press anywhere on the splash moves it along
  document.addEventListener('keydown', (e) => { if (e.key !== 'Enter') return; const btn = current?.querySelector('.splash__btn:not([hidden])'); if (btn) { e.preventDefault(); btn.click(); } });
  show(splash());
  const onward = () => { if (current?.dataset.stage === 'splash') show(pin()); };
  splashTimer = setTimeout(onward, SPLASH);
  root.addEventListener('click', () => { if (current?.dataset.stage === 'splash') onward(); });
  await api.storage.local.set({ 'setup:offered': true }).catch(() => {});
})();
