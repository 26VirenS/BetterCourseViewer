/* The pointers on black: the screen goes black and one thing at a time is pointed at and named,
 * with a Continue that comes in after a few seconds; the last Continue takes the black away.
 * Two runs use it. After the setup, the reloaded page comes back black (the flag is read before
 * the page draws, so the Dashboard is never seen first) and points at the look switch at the top
 * right — an opened, still copy of the real one, Persistent row and all — then at a mock Away
 * Refresh pill counting its three seconds down in slow motion. The first time Tools opens, it says
 * what Tools is, then shows the drag: a card pulled to the top turning into a pin beside the
 * switch. A phone has no switch in its header, so it gets the pointers that need none; the app
 * (no switch, no Away Refresh) never sees the setup's. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const html = document.documentElement;
  const KEY = 'welcome:pending'; // the setup's run, armed for the reloaded page
  const WAIT = 4000; // Continue comes in after this long, on each stage: time to take the pointer in first
  const LEAVE = 260; // a stage's fade-out (app.css: bcv-welcome-out)

  // key → the stage: its layout (app.css: .bcv-welcome__stage[data-stage]), the lines, an arrow
  // (viewBox size, the line, the head) and the thing pointed at, built when the stage opens
  const STAGES = {
    look: {
      layout: 'look', kicker: 'just in case', title: 'Use this to disable Simpl', hint: 'Use Persistent to keep Simpl off for a while',
      arrow: { w: 260, ht: 230, line: 'M40 215C40 150 100 90 150 30', head: 'M111.4 40.6L150 30L146.7 69.9' }, // a curve from the text up to the switch's underside
      prop: (app, ctx) => lookCopy(ctx.look, true),
    },
    away: {
      layout: 'away', kicker: 'Away Refresh', title: 'Click to cancel', hint: 'Away refresh prevents errors that show up after you’ve been gone for a while',
      arrow: { w: 100, ht: 150, line: 'M50 140L50 14', head: 'M28 38L50 14L72 38' },
      prop: (app) => awayMock(app),
    },
    tools: { layout: 'center', title: 'Some helpful things', hint: 'some tools to help you do more, quickly.' },
    pin: {
      layout: 'pin', kicker: 'Tools', title: 'Drag a tool to the top', hint: 'It becomes a small button next to the Simpl Courses switch, on every page.',
      prop: (app, ctx) => pinDemo(app, ctx.look),
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
  async function due() {
    if (self.BCVBridge?.native) { await clear(); return false; } // the app: no switch, no Away Refresh
    try { return (await BCV.api.storage.local.get(KEY))[KEY] === true; } catch { return false; }
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
  const arrowOf = ({ w, ht, line, head }) => h('span', { class: 'bcv-welcome__arrowbox', 'aria-hidden': 'true', html:
    `<svg class="bcv-welcome__arrow" viewBox="0 0 ${w} ${ht}" width="${w}" height="${ht}"><path class="bcv-welcome__line" pathLength="1" d="${line}"/><path class="bcv-welcome__head" d="${head}"/></svg>` });

  /** One stage on the black: the thing pointed at, the arrow, the lines, and Continue after a
   *  while. Resolves when Continue is pressed. */
  function stage(app, key, ctx) {
    const s = STAGES[key];
    const next = h('button', { type: 'button', class: 'bcv-welcome__next', text: 'Continue' });
    next.hidden = true;
    const box = h('div', { class: 'bcv-welcome__stage', dataset: { stage: s.layout } }, [
      s.arrow ? arrowOf(s.arrow) : null,
      h('div', { class: 'bcv-welcome__text' }, [
        s.kicker ? h('div', { class: 'bcv-welcome__kicker', text: s.kicker }) : null,
        h('div', { class: 'bcv-welcome__title', text: s.title }),
        h('div', { class: 'bcv-welcome__hint', text: s.hint }),
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
   *  no keys it is the setup's run (the switch where there is one, then Away Refresh), and its flag
   *  goes; another run says what to do when it ends. */
  async function open(app, keys = null, { onDone = null } = {}) {
    const el = cover();
    // the switch is looked for as each stage starts (a page's first draw comes before it is mounted);
    // a phone's header has none, so the stages that point at it are left out there
    const lookNow = () => { const l = document.getElementById('bcv-look'); return l && getComputedStyle(l).display !== 'none' ? l : null; };
    const setupRun = !keys;
    for (const key of (keys || ['look', 'away']).filter((k) => STAGES[k])) {
      const look = lookNow();
      if (!look && (key === 'look' || key === 'pin')) continue;
      await stage(app, key, { look });
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
