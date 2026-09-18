/* The welcome after the setup: two pointers on a black screen, the first thing the reloaded page
 * shows. Stage one points a big arrow at the look switch at the top right — an opened, still copy of
 * the real one, Persistent row and all, so the switch is exactly where it will be — and says what it
 * is for. Stage two points at a mock Away Refresh pill counting its three seconds down in slow
 * motion. Each stage is the arrow, three lines and a Continue that comes in after four seconds; the
 * second Continue takes the black away. Armed by the setup's Open Canvas (a flag in the extension's
 * storage, read before the page draws so the reload comes back black rather than showing the
 * Dashboard first) and cleared by that last press. A phone has no switch in its header, so it gets
 * the second pointer alone; the app has neither the switch nor Away Refresh and never sees it. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const html = document.documentElement;
  const KEY = 'welcome:pending';
  const WAIT = 4000; // Continue comes in after this long, on each stage: time to take the pointer in first
  const LEAVE = 260; // a stage's fade-out (app.css: bcv-welcome-out)

  const STAGES = {
    look: {
      kicker: 'just in case', title: 'Use this to disable Simpl', hint: 'Use Persistent to keep Simpl off for a while',
      // a curve from the text up to the switch's underside
      arrow: { w: 260, ht: 230, line: 'M40 215C40 150 100 90 150 30', head: 'M111.4 40.6L150 30L146.7 69.9' },
    },
    away: {
      kicker: 'Away Refresh', title: 'Click to cancel', hint: 'Away refresh prevents errors that show up after you’ve been gone for a while',
      arrow: { w: 100, ht: 150, line: 'M50 140L50 14', head: 'M28 38L50 14L72 38' },
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

  /** The look switch as it will be: the real pill copied, opened, with nothing wired. */
  function lookCopy(look) {
    const copy = look.cloneNode(true);
    copy.removeAttribute('id');
    copy.className = 'bcv-look-pill is-open bcv-welcome__look';
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
  const arrowOf = ({ w, ht, line, head }) => h('span', { class: 'bcv-welcome__arrowbox', 'aria-hidden': 'true', html:
    `<svg class="bcv-welcome__arrow" viewBox="0 0 ${w} ${ht}" width="${w}" height="${ht}"><path class="bcv-welcome__line" pathLength="1" d="${line}"/><path class="bcv-welcome__head" d="${head}"/></svg>` });

  /** One stage on the black: the thing pointed at, the arrow, the three lines, and Continue after
   *  a while. Resolves when Continue is pressed. */
  function stage(app, key, look) {
    const s = STAGES[key];
    const next = h('button', { type: 'button', class: 'bcv-welcome__next', text: 'Continue' });
    next.hidden = true;
    const box = h('div', { class: 'bcv-welcome__stage', dataset: { stage: key } }, [
      arrowOf(s.arrow),
      h('div', { class: 'bcv-welcome__text' }, [
        h('div', { class: 'bcv-welcome__kicker', text: s.kicker }),
        h('div', { class: 'bcv-welcome__title', text: s.title }),
        h('div', { class: 'bcv-welcome__hint', text: s.hint }),
      ]),
      next,
    ]);
    const prop = key === 'look' ? lookCopy(look) : awayMock(app);
    ui.el.dataset.stage = key;
    ui.el.replaceChildren(prop, box);
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
    st.prop.classList.add('is-out');
    await wait(LEAVE);
    st.box.remove();
    st.prop.remove();
  }

  /** The whole welcome, from black to the page: the pointers in turn, then the black fades. */
  async function open(app) {
    const el = cover();
    const look = document.getElementById('bcv-look');
    const keys = [];
    if (look && getComputedStyle(look).display !== 'none') keys.push('look'); // a phone's header has no switch to point at
    keys.push('away');
    for (const key of keys) {
      await stage(app, key, look);
      await leave();
    }
    await clear();
    el.classList.add('is-out');
    html.classList.remove('bcv-welcome');
    ui = null;
    setTimeout(() => el.remove(), 400);
  }

  BCV.welcome = { arm, clear, due, cover, open, active };
})();
