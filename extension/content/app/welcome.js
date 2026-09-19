/* The pointers on black: the screen goes black and one thing at a time is pointed at and named,
 * with a Continue that comes in after a few seconds; the last Continue takes the black away.
 * Two runs use it. After the setup, the reloaded page comes back black (the flag is read before
 * the page draws, so the Dashboard is never seen first) and points at the look switch at the top
 * right — a copy of the real one, shown working: a pointer comes to it, presses its stops and
 * drags its knob, the lines under it saying what each part does — then at a mock Away Refresh
 * pill counting its three seconds down in slow motion, then at the Dashboard's way in: a
 * counter pressed, the list behind it, an item previewed beside the list. The first time Tools
 * opens, it says what Tools is, then shows the drag: a card pulled to the top turning into a pin
 * beside the switch. A phone has no switch in its header, so it gets the pointers that need none; the app
 * (no switch, no Away Refresh) never sees the setup's. Anyone who had Simpl before the switch
 * became a slider gets its show alone, once: their What's New mark is from before it. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const html = document.documentElement;
  const KEY = 'welcome:pending'; // the setup's run, armed for the reloaded page
  const KEY2 = 'welcome:look3'; // the switch's show seen (with the setup's run, or alone after an update)
  const LOOK2_SINCE = '2.34.0'; // the show's own version: a What's New mark from before it means the show is owed
  const CURSOR = '<svg viewBox="0 0 24 24" width="26" height="26"><path d="M5 3l14 9-6 1.5 3.5 6.5-2.5 1.5-3.5-6.5L6 19z" fill="#fff" stroke="#1c1c1e" stroke-width="1.4" stroke-linejoin="round"/></svg>';
  const WAIT = 3000; // Continue comes in after this long, on each stage: time to take the pointer in first
  const LEAVE = 260; // a stage's fade-out (app.css: bcv-welcome-out)

  // key → the stage: its layout (app.css: .bcv-welcome__stage[data-stage]), the lines, an arrow
  // (viewBox size, the line, the head) and the thing pointed at, built when the stage opens
  const STAGES = {
    look: {
      layout: 'look', kicker: 'There’s a new Simpl switch.', title: 'Press different parts for different things', hint: ['Left: Simpl is off', 'Middle: Simpl is inactive', 'Right: Simpl is on & active.'], stops: [-1, 0, 1],
      prop: (app, ctx) => lookShow(app, ctx),
    },
    away: {
      layout: 'away', kicker: 'Away Refresh', title: 'Click to cancel', hint: 'Away refresh prevents errors that show up after you’ve been gone for a while',
      arrow: { w: 100, ht: 150, line: 'M50 140L50 14', head: 'M28 38L50 14L72 38' },
      prop: (app) => awayMock(app),
    },
    tools: { layout: 'center', title: 'Some helpful things', hint: 'some tools to help you do more, quickly.' },
    pin: {
      layout: 'demo', kicker: 'Tools', title: 'Drag a tool to the top', hint: 'It becomes a small button next to the Simpl Courses switch, on every page.',
      prop: (app, ctx) => pinDemo(app, ctx.look),
    },
    peek: {
      layout: 'demo', kicker: 'Dashboard', title: 'Press a card, then an item', hint: 'A card opens what is behind its number. An item opens beside the list, so you never leave the page.',
      prop: () => peekDemo(),
    },
  };

  let ui = null; // the welcome on show: { el, stage: { key, box, prop, next } | null, timer }
  const active = () => !!ui;
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));

  /** The setup arms it just before reloading the page. */
  async function arm() {
    try { await BCV.api.storage.local.set({ [KEY]: true }); } catch { /* nothing to arm with */ }
  }
  async function clear() {
    try { await BCV.api.storage.local.remove(KEY); } catch { /* already gone */ }
  }
  const older = (a, b) => { const x = String(a).split('.').map(Number), y = String(b).split('.').map(Number); for (let i = 0; i < 3; i++) { if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) < (y[i] || 0); } return false; };
  /** Which run this page owes: 'setup' (the setup's, armed), 'look' (the switch's show alone, once,
   *  for anyone who had Simpl before the slider), or none. */
  async function due() {
    if (self.BCVBridge?.native) { await clear(); return false; } // the app: no switch, no Away Refresh
    try {
      const f = await BCV.api.storage.local.get([KEY, KEY2, 'setup:done', 'whatsnew:seen']);
      if (f[KEY] === true) return 'setup';
      if (f['setup:done'] && !f[KEY2] && typeof f['whatsnew:seen'] === 'string' && older(f['whatsnew:seen'], LOOK2_SINCE)) return 'look';
    } catch { /* nothing to read: nothing owed */ }
    return false;
  }

  /** The black, now: before the page draws, so the reload comes back black and stays that way. */
  function cover() {
    if (ui) return ui.el;
    const el = h('div', { id: 'bcv-welcome', class: 'bcv-welcome', role: 'dialog', 'aria-modal': 'true', 'aria-label': 'Welcome to Simpl Courses', tabindex: '-1' });
    // Enter (or Space) is Continue once it is there; the black itself holds the focus, so no ring sits on the button
    el.addEventListener('keydown', (e) => {
      if (e.key !== 'Enter' && e.key !== ' ') return;
      const next = ui?.stage?.next;
      if (!next || next.hidden || e.target === next) return;
      e.preventDefault();
      next.click();
    });
    ui = { el, stage: null, timer: 0 };
    html.classList.add('bcv-welcome');
    document.body.append(el);
    el.focus({ preventScroll: true });
    return el;
  }

  /** The look switch as it will be: the real pill copied, opened or folded, with nothing wired. */
  function lookCopy(look, open) {
    const copy = look.cloneNode(true);
    copy.removeAttribute('id');
    copy.className = `bcv-look-pill bcv-welcome__look ${open ? 'is-open' : 'bcv-welcome__look--folded'}`;
    copy.setAttribute('aria-hidden', 'true');
    for (const b of copy.querySelectorAll('button')) { b.tabIndex = -1; b.removeAttribute('title'); }
    return copy;
  }
  /** The switch, shown working: a copy of it at the top right (app.lookDemo: the same DOM, its knob
   *  put where the show says), and a pointer that comes to it (it opens into the slider), presses
   *  its middle (off for this page), its right (on), drags its knob to the left stop (locked) and
   *  presses its right again (unlocked), round and round. With reduced motion: the opened copy, still. */
  function lookShow(app) {
    const demo = app.lookDemo?.();
    if (!demo) return null;
    demo.el.classList.add('bcv-welcome__look');
    const cursor = h('span', { class: 'bcv-welcome__cursor bcv-welcome__cursor--look', 'aria-hidden': 'true', html: CURSOR });
    const wrap = h('div', { class: 'bcv-welcome__lookshow', 'aria-hidden': 'true' }, [demo.el, cursor]);
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) { demo.open(true); return wrap; }
    const alive = () => wrap.isConnected && !wrap.classList.contains('is-out');
    const point = (nx) => { const r = demo.slider.el.getBoundingClientRect(); return { x: r.left + (nx / 208) * r.width, y: r.top + r.height / 2 }; }; // the slider's own x, on the page
    const disc = () => { const r = demo.el.getBoundingClientRect(); return { x: r.right - r.height / 2, y: r.top + r.height / 2 }; };
    const away = () => { const d = disc(); return { x: d.x - 150, y: d.y + 130 }; };
    const cursorTo = ({ x, y }, ms) => { cursor.style.setProperty('--cms', `${ms}ms`); cursor.style.setProperty('--cx', `${x - 5}px`); cursor.style.setProperty('--cy', `${y - 3}px`); };
    const q = (ms, fn) => setTimeout(() => { if (alive()) fn(); }, ms);
    const press = () => { cursor.classList.add('is-press'); q(180, () => cursor.classList.remove('is-press')); };
    const drag = (fromC, toC, ms) => { // the knob and the pointer together, frame by frame
      const t0 = performance.now();
      const step = () => {
        if (!alive()) return;
        const k = Math.min(1, (performance.now() - t0) / ms);
        const e = k < 0.5 ? 2 * k * k : 1 - ((-2 * k + 2) ** 2) / 2;
        const c = fromC + (toC - fromC) * e;
        demo.slider.paint(c - 20);
        cursorTo(point(c), 0);
        if (k < 1) requestAnimationFrame(step);
      };
      requestAnimationFrame(step);
    };
    const loop = () => {
      if (!alive()) return;
      demo.setPos(1, { glide: false });
      demo.open(false);
      cursor.style.opacity = '0';
      cursorTo(away(), 0);
      q(250, () => { cursor.style.opacity = '1'; cursorTo(disc(), 800); });
      q(1150, () => demo.open(true));
      q(2100, () => cursorTo(point(104), 600));
      q(2800, () => { press(); demo.setPos(0); });
      q(4500, () => cursorTo(point(173), 600));
      q(5200, () => { press(); demo.setPos(1); });
      q(6900, () => cursorTo(point(184), 600));
      q(7600, () => { cursor.classList.add('is-press'); drag(184, 24, 1100); });
      q(8800, () => { cursor.classList.remove('is-press'); demo.setPos(-1); });
      q(10400, () => cursorTo(point(173), 600));
      q(11100, () => { press(); demo.setPos(1); });
      q(12300, () => { cursorTo(away(), 700); cursor.style.opacity = '0'; });
      q(12700, () => demo.open(false));
      q(13500, loop);
    };
    q(60, loop); // (once the stage is on the page, so the copy can be measured)
    return wrap;
  }
  /** The Away Refresh pill as it will be, its dial in slow motion (app.css) and nothing wired. */
  function awayMock(app) {
    const btn = app.awayPill();
    btn.tabIndex = -1;
    btn.removeAttribute('aria-label');
    return h('div', { class: 'bcv-welcome__away', 'aria-hidden': 'true' }, btn);
  }
  /** The drag, shown: the folded switch where it is, a tool card that a cursor pulls to the top
   *  right and that shrinks into a pin beside the switch, round and round (app.css). */
  function pinDemo(app, look) {
    const T = BCV.tools;
    const tool = T?.toolOf('pomo') || T?.TOOLS?.[0];
    const card = tool ? T.cardEl(tool, { demo: true, dark: app?.isDark?.() }) : null;
    const pinBtn = tool ? T.pinEl(tool, { demo: true }) : null;
    return h('div', { class: 'bcv-welcome__pindemo', 'aria-hidden': 'true' }, [
      lookCopy(look, false),
      card ? h('div', { class: 'bcv-welcome__democard' }, card) : null,
      h('span', { class: 'bcv-welcome__cursor', html: '<svg viewBox="0 0 24 24" width="26" height="26"><path d="M5 3l14 9-6 1.5 3.5 6.5-2.5 1.5-3.5-6.5L6 19z" fill="#fff" stroke="#1c1c1e" stroke-width="1.4" stroke-linejoin="round"/></svg>' }),
      pinBtn ? h('div', { class: 'bcv-welcome__demopin' }, pinBtn) : null,
    ]);
  }
  /** The dashboard's way in, shown: the three counters at the top, a cursor pressing the middle one,
   *  the sheet of what is behind it rising, a press on its first row, and the preview sliding in
   *  beside the list — drawn as shapes, not numbers, round and round (app.css). */
  function peekDemo() {
    const bar = (cls) => h('span', { class: `bcv-welcome__bar ${cls}` });
    const stat = (label, mid) => h('div', { class: `bcv-welcome__stat ${mid ? 'bcv-welcome__stat--mid' : ''}` }, [
      h('span', { class: 'bcv-welcome__statlabel', text: label }),
      bar('bcv-welcome__bar--num'),
      bar('bcv-welcome__bar--sub'),
    ]);
    const row = (i) => h('div', { class: `bcv-welcome__row ${i === 0 ? 'bcv-welcome__row--first' : ''}` }, [h('span', { class: 'bcv-welcome__dot' }), bar('bcv-welcome__bar--row'), h('span', { class: 'bcv-welcome__chip' })]);
    return h('div', { class: 'bcv-welcome__peek', 'aria-hidden': 'true' }, [
      h('div', { class: 'bcv-welcome__stats' }, [stat('Due today'), stat('Due this week', true), stat('Unread announcements')]),
      h('div', { class: 'bcv-welcome__sheetmock' }, [
        h('div', { class: 'bcv-welcome__sheethead' }, [bar('bcv-welcome__bar--big'), h('span', { class: 'bcv-welcome__sheettitle', text: 'Due this week' })]),
        h('div', { class: 'bcv-welcome__sheetrows' }, [row(0), row(1), row(2)]),
      ]),
      h('div', { class: 'bcv-welcome__pvmock' }, [bar('bcv-welcome__bar--title'), bar('bcv-welcome__bar--line'), bar('bcv-welcome__bar--line'), bar('bcv-welcome__bar--line bcv-welcome__bar--short'), h('span', { class: 'bcv-welcome__pvbtn', text: 'Open' })]),
      h('span', { class: 'bcv-welcome__cursor bcv-welcome__cursor--peek', html: '<svg viewBox="0 0 24 24" width="26" height="26"><path d="M5 3l14 9-6 1.5 3.5 6.5-2.5 1.5-3.5-6.5L6 19z" fill="#fff" stroke="#1c1c1e" stroke-width="1.4" stroke-linejoin="round"/></svg>' }),
    ]);
  }
  const arrowOf = ({ w, ht, line, head }) => h('span', { class: 'bcv-welcome__arrowbox', 'aria-hidden': 'true', html:
    `<svg class="bcv-welcome__arrow" viewBox="0 0 ${w} ${ht}" width="${w}" height="${ht}"><path class="bcv-welcome__line" pathLength="1" d="${line}"/><path class="bcv-welcome__head" d="${head}"/></svg>` });

  /** One stage on the black: the thing pointed at, the arrow, the lines, and Continue after a
   *  while. Resolves when Continue is pressed. */
  function stage(app, key, ctx) {
    const s = STAGES[key];
    const next = h('button', { type: 'button', class: 'bcv-welcome__next', text: 'Continue' });
    next.hidden = true;
    const lines = Array.isArray(s.hint); // (a hint of several lines: one per stop)
    const box = h('div', { class: 'bcv-welcome__stage', dataset: { stage: s.layout } }, [
      s.arrow ? arrowOf(s.arrow) : null,
      h('div', { class: 'bcv-welcome__text' }, [
        s.kicker ? h('div', { class: 'bcv-welcome__kicker', text: s.kicker }) : null,
        h('div', { class: 'bcv-welcome__title', text: s.title }),
        lines ? h('div', { class: 'bcv-welcome__hint bcv-welcome__hint--rows' }, s.hint.map((line, i) => { // (a row per stop, with a small slider showing where it is)
          const at = line.indexOf(':');
          const stop = s.stops?.[i] ?? 0;
          return h('div', { class: 'bcv-welcome__stoprow' }, [
            h('span', { class: 'bcv-welcome__stop', dataset: { stop: String(stop) }, style: { '--c': stop < 0 ? '#ff4f1f' : stop > 0 ? '#34c759' : '#8e8e93' }, 'aria-hidden': 'true' }, h('span', { class: 'bcv-welcome__stopknob' })),
            h('span', { class: 'bcv-welcome__stoptext' }, at > 0 ? [h('b', { text: line.slice(0, at + 1) }), line.slice(at + 1)] : [line]),
          ]);
        })) : h('div', { class: 'bcv-welcome__hint', text: s.hint }),
      ]),
      next,
    ]);
    const prop = s.prop ? s.prop(app, ctx) : null;
    ui.el.dataset.stage = key;
    ui.el.replaceChildren(...[prop, box].filter(Boolean));
    ui.stage = { key, box, prop, next };
    clearTimeout(ui.timer);
    ui.timer = setTimeout(() => {
      if (ui?.stage?.next !== next) return;
      next.hidden = false;
      if (!ui.el.contains(document.activeElement)) ui.el.focus({ preventScroll: true });
    }, WAIT);
    return new Promise((resolve) => next.addEventListener('click', () => resolve(), { once: true }));
  }
  /** Everything on the stage goes; the black stays. */
  async function leave() {
    const st = ui?.stage;
    if (!st) return;
    ui.stage = null;
    st.box.classList.add('is-out');
    st.prop?.classList.add('is-out');
    await wait(LEAVE);
    st.box.remove();
    st.prop?.remove();
  }

  /** The whole run, from black to the page: the stages named, in turn, then the black fades. With
   *  no keys it is the setup's run (the switch where there is one, then Away Refresh, then the
   *  Dashboard's way in), and its flag goes; another run says what to do when it ends. */
  async function open(app, keys = null, { onDone = null } = {}) {
    const el = cover();
    // the switch is looked for as each stage starts (a page's first draw comes before it is mounted);
    // a phone's header has none, so the stages that point at it are left out there
    const lookNow = () => { const l = document.getElementById('bcv-look'); return l && getComputedStyle(l).display !== 'none' ? l : null; };
    const setupRun = !keys;
    for (const key of (keys || ['look', 'away', 'peek']).filter((k) => STAGES[k])) {
      const look = lookNow();
      if (!look && (key === 'look' || key === 'pin')) continue;
      await stage(app, key, { look });
      if (key === 'look') { try { await BCV.api.storage.local.set({ [KEY2]: true }); } catch { /* shown all the same */ } } // (seen: not owed again after an update)
      await leave();
    }
    if (setupRun) await clear();
    else if (onDone) await Promise.resolve(onDone()).catch(() => {});
    el.classList.add('is-out');
    html.classList.remove('bcv-welcome');
    ui = null;
    setTimeout(() => el.remove(), 400);
  }

  BCV.welcome = { arm, clear, due, cover, open, active };
})();
