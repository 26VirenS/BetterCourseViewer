/* External tools, in a tab of their own with the interface's bar over them.
 *
 * A course's Campus tools, a module's external tool or link, an assignment's tool, the school's own
 * nav tools: Canvas draws them on a page of its own or sends them to a new tab. They used to be
 * framed in a popup over the page, and that was the wrong shape for what they are. A tool is a
 * whole site: it signs you in, sets cookies, opens windows of its own, hands back to the window
 * that launched it. Framed, every one of those had to be nursed through — a window taken from the
 * page before the browser could make it, a tab caught afterwards and handed back, a wait for a
 * sign-in to settle — and a sign-in that did anything unusual still broke.
 *
 * So the tool gets a tab. What is kept is the part that was worth keeping: it does not feel like
 * being dropped out of the interface. The extension puts its own bar across the top of the tool's
 * own page (content/toolbar.js), with the tool's name, the look switch, Reload, and an X that
 * closes the tab and goes back to the Canvas tab it came from. It reads like the popup and is not
 * one. A tab the tool opens for itself gets the same bar, so a sign-in that goes through three
 * sites keeps it the whole way.
 *
 * What is left here is the deciding: which address a tool link leads to, and the look switch, which
 * also turns over the one thing still framed on the page (the graphing tool's own board).
 */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;

  // ---- the look, for anything of somebody else's shown here ----------------------------------
  // Dark unless the sun says otherwise. A frame is another origin and none of its styling is ours
  // to set, so dark means the page is turned over; an inversion is not a dark theme, so the hue
  // goes back round with it and colours land near where they started. The tool's own tab reads the
  // same setting (content/toolbar.js), which is why it is kept in the extension's storage.
  const THEME_KEY = 'ext:theme';
  let theme = 'dark';
  function paintTheme() {
    document.documentElement.dataset.bcvExtTheme = theme;
    const to = theme === 'dark' ? 'Light appearance' : 'Dark appearance';
    for (const b of document.querySelectorAll('.bcv-ext__theme')) { b.title = to; b.setAttribute('aria-label', to); }
  }
  function setTheme(next) {
    theme = next === 'light' ? 'light' : 'dark';
    paintTheme();
    try { BCV.api?.storage?.local?.set?.({ [THEME_KEY]: theme }); } catch { /* the page keeps its own */ }
  }
  /** The sun (press for light) or the moon (press for dark). */
  const themeButton = () => {
    const b = h('button', { type: 'button', class: 'bcv-btn bcv-ext__theme', onclick: () => setTheme(theme === 'dark' ? 'light' : 'dark') }, [
      U.svg(IC.sun, { size: 14, stroke: 'currentColor', width: 1.9, cls: 'bcv-ext__sun' }),
      U.svg(IC.moon, { size: 14, stroke: 'currentColor', width: 1.9, cls: 'bcv-ext__moon' }),
    ]);
    const to = theme === 'dark' ? 'Light appearance' : 'Dark appearance';
    b.title = to;
    b.setAttribute('aria-label', to);
    return b;
  };
  paintTheme();
  (async () => {
    try { const r = await BCV.api.storage.local.get(THEME_KEY); if (r[THEME_KEY]) theme = r[THEME_KEY] === 'light' ? 'light' : 'dark'; } catch { /* it stays dark */ }
    paintTheme();
  })();

  /** Canvas's launch page for one of its tools, without Canvas's chrome round it. */
  function borderless(href) {
    const u = new URL(href, location.origin);
    if (u.origin === location.origin && /\/external_tools\//.test(u.pathname) && !u.searchParams.has('display')) u.searchParams.set('display', 'borderless');
    return u.href;
  }
  /** Canvas's own launch page, told to stand the interface down: the tab is the tool's, not ours. */
  function launched(href) {
    const u = new URL(href, location.origin);
    if (u.origin === location.origin) u.searchParams.set('bcv', 'tool');
    return u.href;
  }

  // ---- every site, asked for once (2.99.141) ----------------------------------------------------
  // A tool opens on its own site, and Simpl's bar can follow it there only where the browser lets the
  // extension in. Chrome's quiet build, Chrome with its site access cut back, and Safari have not been
  // let onto every site — and a page cannot ask for that itself: the browser takes the request only
  // from the extension's own panel. So the first time a tool is opened there, a card points at where
  // the extension sits in the browser (Chrome's puzzle piece at the top right, Safari's button beside
  // the address bar) and says what to press — Allow on every site, in its panel — and waits. Allowed,
  // the tool opens; Open without it opens it as before. Either way the card is not shown again (the
  // panel keeps the button for later). Nothing here where no background answers (the apps).
  let allKnown = false; // (allowed, or the card seen: no asking again from this page)
  let coachUp = null; // the card up, and the tool it is holding
  /** The background asked, or null where it does not answer in time (a tool never waits long on it). */
  function ask(msg, ms = 1500) {
    return new Promise((resolve) => {
      let done = false;
      const end = (r) => { if (done) return; done = true; clearTimeout(t); resolve(r || null); };
      const t = setTimeout(() => end(null), ms);
      try { Promise.resolve(BCV.api.runtime.sendMessage(msg)).then(end, () => end(null)); } catch { end(null); }
    });
  }
  /** The browser, as far as where its extensions sit goes. */
  function browserKind() {
    const ua = navigator.userAgent || '';
    if (/iPhone|iPad|iPod/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1 && /Safari\//.test(ua) && !/Chrome\//.test(ua))) return 'ios';
    if (/Edg\//.test(ua)) return 'edge';
    if (/Firefox\//.test(ua)) return 'firefox';
    if (/Safari\//.test(ua) && !/(Chrome|Chromium|CriOS)\//.test(ua)) return 'safari';
    return 'chrome';
  }
  const PUZZLE = 'M10 4.5a2 2 0 014 0V6h4v4h-1.5a2 2 0 000 4H18v4h-4v-1.5a2 2 0 00-4 0V18H6v-4h1.5a2 2 0 000-4H6V6h4z';
  /** Before a tool's first opening: on its way at once where the extension may run on every site (or
   *  the card has been seen); else the card, which settles whether it opens (true) or not. */
  async function everySiteFirst(opts) {
    if (allKnown || !BCV.api?.runtime?.id) return true;
    const st = await ask({ type: 'allSitesState' });
    if (!st || st.ok !== true || st.all !== false || st.seen) {
      if (st?.ok && (st.all || st.seen)) allKnown = true;
      return true;
    }
    if (coachUp) { coachUp.opts = opts; coachUp.say(); return false; } // (another tool pressed meanwhile: the card holds that one now)
    return coach(opts);
  }
  /** The card: where the extension sits, the three presses, and a wait for the browser's yes. It opens
   *  the tool itself (allowed, or Open without it) and resolves false: nothing left for the caller. */
  function coach(opts) {
    return new Promise((resolve) => {
      const kind = browserKind();
      const name = { chrome: 'Chrome', edge: 'Edge', firefox: 'Firefox', safari: 'Safari', ios: 'Safari' }[kind];
      const b = (t) => h('b', { text: t });
      const puzzle = () => h('span', { class: 'bcv-allsites__glyph', 'aria-label': 'the puzzle piece' }, U.svg(PUZZLE, { size: 13, stroke: 'currentColor', width: 2 }));
      const first = kind === 'safari' ? ['Press ', b('Simpl Courses'), ' beside the address bar, up here.']
        : kind === 'ios' ? ['Press the puzzle piece ', puzzle(), ' in the address bar, then ', b('Simpl Courses'), '.']
          : ['Press ', b('Simpl Courses'), ' at the top right — or the puzzle piece ', puzzle(), ' there, then ', b('Simpl Courses'), '.'];
      const step = (n, kids) => h('li', { class: 'bcv-allsites__step', dataset: { step: String(n) } }, [h('span', { class: 'bcv-allsites__num', text: String(n), 'aria-hidden': 'true' }), h('span', { class: 'bcv-allsites__say' }, kids)]);
      const kicker = h('span', { class: 'bcv-allsites__kicker' });
      const status = h('div', { class: 'bcv-allsites__status', role: 'status' }, [h('span', { class: 'bcv-allsites__spin', 'aria-hidden': 'true' }), h('span', { class: 'bcv-allsites__wait', text: 'Waiting for you to allow it…' })]);
      const arrow = h('span', { class: 'bcv-allsites__arrow', 'aria-hidden': 'true' }, U.svg(kind === 'safari' ? 'M12 20V4M5 11l7-7 7 7' : kind === 'ios' ? 'M12 4v16M5 13l7 7 7-7' : 'M5 19L19 5M10 5h9v9', { size: 30, stroke: 'currentColor', width: 2.4 }));
      const close = h('button', { type: 'button', class: 'bcv-allsites__later', text: 'Not now' });
      const skip = h('button', { type: 'button', class: 'bcv-allsites__skip', text: 'Open without it' });
      const card = h('div', { class: `bcv-allsites is-${kind === 'safari' ? 'top' : kind === 'ios' ? 'bottom' : 'right'}`, role: 'dialog', 'aria-label': 'Allow Simpl on every site', tabindex: '-1', dataset: { browser: kind } }, [
        arrow,
        h('div', { class: 'bcv-allsites__head' }, [kicker]),
        h('div', { class: 'bcv-allsites__title', text: 'Allow Simpl on every site' }),
        h('p', { class: 'bcv-allsites__why', text: 'Course tools open on their own sites. Allowed there, Simpl’s bar — Back to Canvas and your pinned tools — stays over them. Asked once.' }),
        h('ol', { class: 'bcv-allsites__steps' }, [
          step(1, first),
          step(2, ['Press ', b('Allow on every site'), ' in its panel.']),
          step(3, ['Choose ', b('Allow'), ` when ${name} asks.`]),
        ]),
        status,
        h('div', { class: 'bcv-allsites__foot' }, [close, skip]),
      ]);
      const held = { opts, say: () => { kicker.textContent = `Opening ${held.opts.title || 'a tool'}`; } };
      held.say();
      coachUp = held;
      let poll = 0, over = false;
      const onKey = (e) => { if (e.key === 'Escape') { e.preventDefault(); end(false); } };
      function end(go, seen = true) {
        if (over) return;
        over = true;
        clearInterval(poll);
        document.removeEventListener('keydown', onKey, true);
        if (seen) ask({ type: 'allSitesSeen' });
        allKnown = true;
        coachUp = null;
        card.classList.add('is-out');
        setTimeout(() => card.remove(), 220);
        resolve(false); // (the card opens the tool itself: the one it holds now, which a later press may have changed)
        if (go) launch(held.opts);
      }
      close.addEventListener('click', () => end(false));
      skip.addEventListener('click', () => end(true));
      document.addEventListener('keydown', onKey, true);
      document.body.append(card);
      requestAnimationFrame(() => card.classList.add('is-in'));
      try { card.focus({ preventScroll: true }); } catch { /* it shows anyway */ }
      ask({ type: 'allSitesAsk' }); // (the panel puts its button first, marked as the one the page sent you to)
      // the browser's yes lands in the panel, not here: asked after, every second, until it has
      poll = setInterval(async () => {
        if (over) return;
        if (!card.isConnected) { end(false, false); return; }
        const st = await ask({ type: 'allSitesState' }, 1200);
        if (over || !st?.ok || !st.all) return;
        clearInterval(poll);
        card.classList.add('is-done');
        card.querySelector('.bcv-allsites__wait').textContent = `Allowed. Opening ${held.opts.title || 'the tool'}…`;
        setTimeout(() => end(true), 900);
      }, 1000);
    });
  }

  /** Open a tool in a tab of its own, with the bar over it — (2.99.141) the first time where Simpl may
   *  not run on every site, after the card that asks for it. */
  function open(opts = {}) {
    if (!opts.url) return null;
    if (allKnown) return launch(opts);
    everySiteFirst(opts).then((go) => { if (go) launch(opts); }, () => launch(opts));
    return null;
  }
  function launch({ title = 'External tool', url, page = null, newTab = null, note = '', from = null, icon = null } = {}) {
    if (!url) return null;
    const target = launched(url);
    if (from) try { from.blur?.(); } catch { /* it went */ }
    try {
      if (!BCV.api.runtime?.id) throw new Error('gone'); // (the extension was updated under this page: its runtime is gone until the page reloads)
      BCV.api.runtime.sendMessage({ type: 'openTool', url: target, title, note });
    } catch {
      window.open(newTab || page || url, '_blank', 'noopener'); // no background to ask: the browser's own new tab, bare
      try { BCV.ui?.toast?.('Simpl Courses was updated. Reload this page for its bar over tools.'); } catch { /* the tab is open anyway */ }
    }
    return null;
  }
  /** A link to a tool, as Canvas gives it: Canvas's own tool pages launch borderless, any other address as it is. */
  function openLink({ title, href, from = null, icon = null, note = '' } = {}) {
    let u;
    try { u = new URL(href, location.origin); } catch { return null; }
    const own = u.origin === location.origin;
    return open({ title, url: own ? borderless(u.href) : u.href, page: own ? u.pathname + u.search : null, newTab: u.href, from, icon, note });
  }
  /** Is this address one Canvas launches a tool from? */
  const isToolHref = (href) => { try { const u = new URL(href, location.origin); return u.origin === location.origin && /\/external_tools\/(\d+|retrieve)\b/.test(u.pathname) && !u.searchParams.has('bcv'); } catch { return false; } };
  /** Is this page a tool's own tab rather than the interface's? (the shell stands down on it) */
  const isToolTab = () => { try { return new URLSearchParams(location.search).get('bcv') === 'tool'; } catch { return false; } };

  BCV.exttool = {
    open, openLink, isToolHref, isToolTab, borderless, launched,
    theme: { get: () => theme, set: setTheme, button: themeButton },
  };
})();
