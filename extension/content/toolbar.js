/* The interface's bar over a tool's own page.
 *
 * An external tool is not framed any more. Canvas hands over an address, the tool opens in a tab of
 * its own, and this puts the bar across the top of it: the tool's name, where it is, the look
 * switch, Reload, Hide, and the X — which closes the tab and goes back to the Canvas tab it came
 * from. Hide slides the bar up out of the way and gives the tool the whole tab; a small tab left at
 * the top brings it back, and every tool's tab after keeps whichever was chosen last.
 * It reads like the popup it replaces and is not one, which is the point: the tool has a whole tab,
 * its own address bar, its own cookies and its own windows, so a sign-in that wants all of that
 * simply works instead of being nursed through a frame.
 *
 * It sleeps on every page in the world until the background says this tab is a tool's. A tab the
 * tool opens for itself is the same thing again — the background adopts it — so a sign-in that goes
 * through three sites keeps the bar the whole way, and the X at the end still lands on Canvas.
 *
 * Nothing here belongs to the page it sits on: the bar lives in a shadow root with its own styles,
 * outside <body>, so the tool's CSS cannot reach it and the look switch can turn the tool's page
 * over without turning the bar over with it.
 */
(function () {
  const api = (typeof browser !== 'undefined' && browser.runtime) ? browser : (typeof chrome !== 'undefined' && chrome.runtime ? chrome : null);
  if (!api?.runtime?.sendMessage) return;
  if (window.top !== window.self) return; // the bar belongs to the tab, not to a frame inside it
  if (self.__bcvToolbar) return; // once: a site's own registration and the manifest's match can both bring it
  self.__bcvToolbar = true;

  const H = 52; // the bar's height, which the page is pushed down by
  const THEME_KEY = 'ext:theme';
  const HIDE_KEY = 'ext:barHidden'; // (the bar tucked away: one choice for every tool's tab, kept until it is brought back)
  const IC = {
    up: 'M6 15l6-6 6 6',
    down: 'M6 9l6 6 6-6',
    close: 'M6 6l12 12M18 6L6 18',
    reload: 'M4 12a8 8 0 108-8M4 4v5h5',
    sun: 'M12 7.3a4.7 4.7 0 100 9.4 4.7 4.7 0 000-9.4zM12 3.3v1.3M12 19.4v1.3M4.6 12H3.3M20.7 12h-1.3M6.9 6.9L6 6M18 18l-.9-.9M17.1 6.9L18 6M6 18l.9-.9',
    moon: 'M20.1 14.7A8.4 8.4 0 019.3 3.9 8.4 8.4 0 1020.1 14.7z',
    warn: 'M12 4l9 16H3zM12 10v4M12 17h.01',
    shield: 'M11.5 3.4a1.4 1.4 0 011 0l5.5 2.1c.6.2 1 .8 1 1.4v4.7c0 4.4-2.9 7.5-7 8.7a1.4 1.4 0 01-.8 0c-4.2-1.2-7.1-4.3-7.1-8.7V6.9c0-.6.4-1.2 1-1.4z',
  };
  const NS = 'http://www.w3.org/2000/svg';
  function svg(d, { size = 14, width = 1.9, cls = '' } = {}) {
    const s = document.createElementNS(NS, 'svg');
    s.setAttribute('viewBox', '0 0 24 24');
    s.setAttribute('width', String(size));
    s.setAttribute('height', String(size));
    s.setAttribute('fill', 'none');
    s.setAttribute('stroke', 'currentColor');
    s.setAttribute('stroke-width', String(width));
    s.setAttribute('stroke-linecap', 'round');
    s.setAttribute('stroke-linejoin', 'round');
    if (cls) s.setAttribute('class', cls);
    const p = document.createElementNS(NS, 'path');
    p.setAttribute('d', d);
    s.append(p);
    return s;
  }
  const el = (tag, cls, text) => { const n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };

  const CSS = `
:host { all: initial; }
* { box-sizing: border-box; font-family: -apple-system, BlinkMacSystemFont, "SF Pro Text", "Segoe UI", system-ui, sans-serif; }
.bar { position: fixed; top: 0; left: 0; right: 0; height: ${H}px; z-index: 2147483647; display: flex; align-items: center; gap: 10px; padding: 0 12px 0 14px;
  background: #1c1c1e; color: #f2f2f7; border-bottom: 1px solid rgba(255,255,255,.1); box-shadow: 0 1px 12px rgba(0,0,0,.35);
  transition: background .18s ease, transform .34s cubic-bezier(.32,.72,0,1), visibility 0s linear 0s; }
/* tucked away: the bar slides up out of the tab and a small tab at the top brings it back (never while
   a sign-in is under way: the notice and the way out matter more then) */
:host([data-hidden]:not([data-state="auth"])) .bar { transform: translateY(-100%); box-shadow: none; visibility: hidden;
  transition: background .18s ease, transform .34s cubic-bezier(.32,.72,0,1), box-shadow .2s ease, visibility 0s linear .34s; }
.peek { position: fixed; top: 0; left: 50%; transform: translateX(-50%); z-index: 2147483647; display: none; place-items: center; width: 64px; height: 16px; padding: 0;
  border: 1px solid rgba(255,255,255,.18); border-top: 0; border-radius: 0 0 10px 10px; background: rgba(58,58,60,.92); color: #f2f2f7; box-shadow: 0 1px 6px rgba(0,0,0,.3); cursor: pointer;
  transition: height .18s ease, background .18s ease; }
.peek:hover, .peek:focus-visible { height: 22px; background: #0a84ff; border-color: #0a84ff; }
.peek:focus-visible { outline: 2px solid #0a84ff; outline-offset: 1px; }
:host([data-hidden]:not([data-state="auth"])) .peek { display: grid; }
@media (prefers-reduced-motion: reduce) { .bar, :host([data-hidden]) .bar { transition: background .18s ease, visibility 0s; } }
.tile { flex: none; width: 32px; height: 32px; border-radius: 10px; display: grid; place-items: center; background: rgba(10,132,255,.18); color: #0a84ff; }
.titles { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 1px; }
.title { font: 600 14px/1.25 inherit; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.note { font: 500 11.5px/1.25 inherit; color: #a1a1a6; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.acts { flex: none; display: flex; align-items: center; gap: 8px; }
.btn { display: inline-flex; align-items: center; gap: 6px; height: 30px; padding: 0 11px; border: 0; border-radius: 9px; background: rgba(255,255,255,.1); color: #f2f2f7;
  font: 600 12.5px/1 inherit; cursor: pointer; }
.btn:hover { background: rgba(255,255,255,.18); }
.btn:focus-visible { outline: 2px solid #0a84ff; outline-offset: 1px; }
.icon { width: 30px; padding: 0; justify-content: center; }
.x { flex: none; width: 30px; height: 30px; border: 0; border-radius: 50%; background: rgba(255,255,255,.12); color: #f2f2f7; display: grid; place-items: center; cursor: pointer; }
.x:hover { background: #ff453a; color: #fff; }
.x:focus-visible { outline: 2px solid #0a84ff; outline-offset: 1px; }
.auth { position: fixed; top: ${H}px; left: 0; right: 0; z-index: 2147483647; display: none; align-items: center; gap: 12px; padding: 12px 18px;
  background: #7a1419; color: #fff; font: 700 20px/1.25 inherit; }
:host([data-state="auth"]) .auth { display: flex; }
:host([data-state="auth"]) .bar { background: #5c0f13; }
.sun, .moon { display: none; }
:host([data-theme="dark"]) .sun { display: block; }
:host([data-theme="light"]) .moon { display: block; }
`;

  let host = null;
  let root = null;
  let authH = 0; // how far the authenticating strip pushes the page down, on top of the bar
  let hidden = false; // the bar tucked away (HIDE_KEY)
  const tucked = () => hidden && host?.dataset.state !== 'auth'; // (a sign-in under way brings the bar back while it lasts)

  /** Push the page down by whatever the bar and its notice take up, so nothing sits under them. */
  function push() {
    try {
      const top = tucked() ? 0 : H + authH;
      if (top) document.documentElement.style.setProperty('margin-top', `${top}px`, 'important');
      else document.documentElement.style.removeProperty('margin-top'); // (tucked away: the page has the whole tab, its own margin back)
      document.documentElement.style.setProperty('--bcv-toolbar-h', `${top}px`); // (what the tray's own popups start below)
      document.documentElement.classList.toggle('bcv-toolbar-hidden', tucked()); // (the pinned tools go up with the bar)
      // where the bar's own buttons begin, from its right edge: the tray (the pinned tools) sits just left of them, whatever the window's width
      const bar = root?.querySelector('.bar'), acts = root?.querySelector('.acts'), x = root?.querySelector('.x');
      if (bar && acts && x) {
        const left = Math.min(acts.getBoundingClientRect().left || Infinity, x.getBoundingClientRect().left || Infinity);
        if (Number.isFinite(left)) document.documentElement.style.setProperty('--bcv-toolbar-right', `${Math.round(bar.getBoundingClientRect().right - left + 14)}px`);
      }
    } catch { /* the page went */ }
    room();
  }
  try { window.addEventListener('resize', () => { if (host) push(); }); } catch { /* no window to watch */ }

  /* Room for the bar. The margin above moves everything in the page's flow, and nothing a site pins
   * to the window: a header fixed at the top, a sidebar or a whole app fixed from top to bottom, a
   * heading stuck to the top as the page scrolls, a layer placed against the page's very top, a page
   * built to the window's exact height. Those would sit under the bar (or lose their foot below the
   * window). So the strip the bar covers is looked at — on a load, a change to the page, a scroll, a
   * resize — and whatever of the page's own is found there is moved down by just as much as it is
   * covered; whatever filled the window from top to bottom is made that much shorter, so its foot
   * stays in view. It is moved with its margin (a sticky heading with its offset), not its position,
   * so a site's own moves — a header that slides away on scroll — keep working. Every change is
   * written down and given back exactly when the bar is tucked away. */
  const held = new Map(); // element → { was: { prop: [value, priority] }, wrote: { prop: value }, ... }
  const docEl = () => document.documentElement;
  function keep(n, m, prop, value) {
    if (!(prop in m.was)) m.was[prop] = [n.style.getPropertyValue(prop), n.style.getPropertyPriority(prop)];
    if (n.style.getPropertyValue(prop) !== value || n.style.getPropertyPriority(prop) !== 'important') n.style.setProperty(prop, value, 'important');
    m.wrote[prop] = n.style.getPropertyValue(prop); // (as the browser writes it back)
  }
  function giveBack(n, m) {
    for (const [prop, [value, pri]] of Object.entries(m.was)) {
      if (n.style.getPropertyValue(prop) !== m.wrote[prop]) continue; // (the site has written its own since: it stands)
      n.style.removeProperty(prop);
      if (value) n.style.setProperty(prop, value, pri);
    }
    if (!m.hadStyle && !n.style.length) n.removeAttribute('style'); // (no attribute left that the site never wrote)
  }
  function unroom() {
    for (const [n, m] of held) { try { giveBack(n, m); } catch { /* gone */ } }
    held.clear();
  }
  /** The element a covered point belongs to that the margin does not move, if any (memo: one look
   *  per element per scan, the points sharing most of their ancestors). */
  function pinnedOf(e, memo) {
    const body = document.body;
    const path = [];
    let found = null;
    for (let n = e; n && n !== docEl(); n = n.parentElement) {
      if (memo.has(n)) { found = memo.get(n); break; }
      path.push(n);
      const pos = getComputedStyle(n).position;
      if (pos === 'fixed' || pos === 'sticky') { found = n; break; }
      if (pos === 'absolute') {
        const op = n.offsetParent; // (placed against the page's own top, which the margin does not move)
        if (!op || op === docEl() || (op === body && getComputedStyle(body).position === 'static')) { found = n; break; }
      }
      if (n === body) break;
    }
    for (const n of path) memo.set(n, found);
    return found;
  }
  function moveDown(n, top) {
    let m = held.get(n);
    const cs = getComputedStyle(n);
    if (!m) {
      const r = n.getBoundingClientRect();
      const pos = cs.position;
      const docTop = pos === 'absolute' ? r.top + window.scrollY : r.top; // (a layer placed on the page scrolls with it)
      if (pos === 'sticky' && (cs.top === 'auto' || Math.abs(r.top - parseFloat(cs.top)) > 1)) return; // (sticky, but not stuck at the top: it scrolls by like the rest)
      if (pos === 'absolute' && docTop >= top) return; // (on the page below the bar: only scrolled under it, as any of the page is)
      m = { was: {}, wrote: {}, hadStyle: n.hasAttribute('style'), pos, rTop: docTop, top0: parseFloat(cs.top) || 0, margin0: parseFloat(cs.marginTop) || 0, gap: window.innerHeight - r.height, full: r.height >= window.innerHeight * 0.5 && r.bottom <= window.innerHeight + 1, by: -1 };
      held.set(n, m);
    }
    const by = Math.max(0, top - m.rTop);
    if (by === m.by) return;
    m.by = by;
    if (m.pos === 'sticky') keep(n, m, 'top', `${m.top0 + by}px`);
    else keep(n, m, 'margin-top', `${m.margin0 + by}px`);
    // it filled the window to its foot: as much shorter as it moved down, its foot still in view
    if (m.full && (m.wrote.height || n.getBoundingClientRect().bottom > window.innerHeight + 1)) keep(n, m, 'height', `calc(100vh - ${Math.round(m.gap + by)}px)`);
  }
  /** A page built to the window's height (an app: html and body at 100%, a root at 100vh) is pushed
   *  down whole and loses its foot below the window: it is made as much shorter as it was pushed. */
  function shortenShell(top) {
    const vh = window.innerHeight;
    let n = docEl();
    for (let depth = 0; n && depth < 6; depth++) {
      const m0 = held.get(n);
      const r = n.getBoundingClientRect();
      const cs = getComputedStyle(n);
      const flow = cs.position === 'static' || cs.position === 'relative';
      const fit = `calc(100vh - ${top}px)`;
      if (m0?.shell) {
        for (const p of Object.keys(m0.wrote)) keep(n, m0, p, fit); // (the bar's height changed)
      } else if (flow && Math.abs(r.height - vh) <= 1 && r.bottom > vh + 1) {
        const m = { was: {}, wrote: {}, hadStyle: n.hasAttribute('style'), shell: true };
        held.set(n, m);
        // at least the window's height (it grows with what is in it): that floor comes down; exactly the
        // window's height: the height itself
        if ((parseFloat(cs.minHeight) || 0) >= vh - 1) keep(n, m, 'min-height', fit);
        if (Math.abs(n.getBoundingClientRect().height - vh) <= 1) keep(n, m, 'height', fit);
      }
      // down the page's spine: its body, then the tallest box in the flow of each
      if (n === docEl()) { n = document.body; continue; }
      let next = null, tall = 0;
      for (const c of n.children) {
        if (c === host) continue;
        const p = getComputedStyle(c).position;
        if (p !== 'static' && p !== 'relative') continue;
        const h = c.getBoundingClientRect().height;
        if (h > tall) { tall = h; next = c; }
      }
      n = tall >= vh * 0.5 ? next : null;
    }
  }
  function room() {
    try {
      const top = host && !tucked() ? H + authH : 0;
      if (!top) { unroom(); return; }
      if (!document.body) return;
      // what the site has moved on since: let go (a removed element; one whose margin or offset it rewrote)
      for (const [n, m] of held) {
        if (!n.isConnected || Object.entries(m.wrote).some(([p, v]) => n.style.getPropertyValue(p) !== v)) held.delete(n);
      }
      const W = window.innerWidth, seen = new Set(), memo = new Map();
      for (const y of [1, Math.round(top / 2), top - 2]) {
        for (let i = 0; i < 9; i++) {
          for (const e of document.elementsFromPoint(((i + 0.5) * W) / 9, y)) {
            if (e === docEl() || !document.body.contains(e)) continue; // (the bar, the pinned tools and their popups live outside <body>)
            const p = pinnedOf(e, memo);
            if (p && !seen.has(p)) { seen.add(p); moveDown(p, top); }
          }
        }
      }
      for (const [n, m] of held) if (!m.shell && !seen.has(n)) moveDown(n, top); // (the bar's height changed: those already moved follow)
      shortenShell(top);
    } catch { /* the page went */ }
  }
  let roomT = 0, roomAt = 0;
  function roomSoon() {
    if (!host || roomT || tucked()) return;
    roomT = setTimeout(() => { roomT = 0; roomAt = Date.now(); room(); }, Math.max(60, 220 - (Date.now() - roomAt)));
  }
  function watchRoom() {
    try {
      new MutationObserver(roomSoon).observe(docEl(), { childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'class', 'hidden'] });
      window.addEventListener('scroll', roomSoon, { passive: true });
      window.addEventListener('load', roomSoon);
    } catch { /* looked at on the bar's own moves only */ }
  }
  /** The look switch turns <body> over with a filter, which changes what fixed layers are placed
   *  against: everything is given back and looked at afresh. */
  function reroom() { unroom(); room(); }

  // The look switch turns the tool's page over rather than the whole document: the bar lives outside
  // <body>, so it keeps its own colours while the page inside goes dark. An inversion is not the
  // site's own dark theme, so the hue goes back round with it and colours land near where they were.
  let theme = 'dark';
  function paint() {
    if (!host) return;
    host.dataset.theme = theme;
    const on = theme === 'dark';
    try {
      if (document.body) document.body.style.setProperty('filter', on ? 'invert(1) hue-rotate(180deg)' : '', on ? 'important' : '');
      document.documentElement.style.setProperty('background', on ? '#111' : '', on ? 'important' : '');
    } catch { /* the page went */ }
    reroom();
    const to = on ? 'Light appearance' : 'Dark appearance';
    const b = root?.querySelector('.theme');
    if (b) { b.title = to; b.setAttribute('aria-label', to); }
  }
  function setTheme(next) {
    theme = next === 'light' ? 'light' : 'dark';
    paint();
    try { api.storage?.local?.set?.({ [THEME_KEY]: theme }); } catch { /* the tab keeps its own */ }
  }

  /* The pinned tools, here too.
   *
   * The tray beside the look switch is the one piece of the interface that is meant to follow you
   * about — a calculator, the timer, the periodic table, a citation to write down — and a tool's tab
   * is exactly where that is wanted: you are reading somebody else's page and you still want your
   * calculator. So the tray's own scripts are asked for and dropped into this tab, and it mounts in
   * the bar beside the X. Nothing else of the interface comes with them: no shell, no Canvas, no
   * pages. What the tray needs and does not have here is stood in for below — what a page's rich
   * text would be drawn with, what the appearance is, what site this is. A tool that wants Canvas
   * (Grade needed) already copes with not reaching it, and says so where it would have said a
   * number. Everything a tool keeps is in the extension's storage, so a pin is the same pin here.
   */
  async function widgets() {
    const BCV = (self.BCV = self.BCV || {});
    if (BCV.tools) return; // already here
    BCV.overlayRoot = document.documentElement; // (outside <body>, so the look switch cannot turn them over)
    BCV.screens = BCV.screens || {};
    BCV.screens.course = BCV.screens.course || {
      // the one thing the tray borrows from the pages: rich text, which only a picker's own options
      // would use here. Plain and safe: the words, no markup of somebody else's carried in.
      prose: (h2, { cls = '' } = {}) => {
        const n = document.createElement('div');
        n.className = `bcv-prose ${cls}`.trim();
        const d = new DOMParser().parseFromString(String(h2 || ''), 'text/html');
        n.textContent = d.body.textContent || '';
        return n;
      },
    };
    BCV.app = BCV.app || { isDark: () => theme === 'dark', state: {}, siteName: () => location.hostname.replace(/^www\./, '') };
    try {
      const r = await api.runtime.sendMessage({ type: 'toolWidgets' });
      if (!r?.ok) return;
      document.documentElement.dataset.bcvTheme = 'dark'; // the tray keeps the bar's own colours whatever the tool's page is doing
      document.documentElement.classList.add('bcv-tooltab');
      BCV.tools?.mountTray?.();
    } catch { /* the tray simply is not here */ }
  }

  function setState(state) {
    if (!host) return;
    host.dataset.state = state === 'auth' ? 'auth' : 'ready';
    authH = state === 'auth' ? (root?.querySelector('.auth')?.offsetHeight || 52) : 0;
    push();
  }

  /** Tuck the bar away or bring it back. The choice is one for every tool's tab (kept in the
   *  extension's storage, so the next tool opens the way the last was left); the small tab at the
   *  top of the page is always there to bring it back. */
  function setHidden(next, { save = true } = {}) {
    hidden = !!next;
    if (host) {
      if (hidden) host.dataset.hidden = ''; else delete host.dataset.hidden;
      // the press that hid it leaves the keyboard on the tab that brings it back, and the other way round
      const had = root?.activeElement;
      if (had && (had.classList.contains('hide') || had.classList.contains('peek'))) root.querySelector(hidden ? '.peek' : '.hide')?.focus({ preventScroll: true });
    }
    push();
    if (save) { try { api.storage?.local?.set?.({ [HIDE_KEY]: hidden }); } catch { /* this tab keeps it */ } }
  }

  function build(tool) {
    host = document.createElement('bcv-tool-bar');
    host.setAttribute('data-bcv-tool-bar', '');
    root = host.attachShadow({ mode: 'open' });
    const style = document.createElement('style');
    style.textContent = CSS;

    const tile = el('div', 'tile');
    tile.append(svg(IC.shield, { size: 16, width: 2 }));
    const titles = el('div', 'titles');
    titles.append(el('div', 'title', tool.title || location.hostname), el('div', 'note', location.hostname.replace(/^www\./, '')));

    const themeBtn = el('button', 'btn icon theme');
    themeBtn.type = 'button';
    themeBtn.append(svg(IC.sun, { cls: 'sun' }), svg(IC.moon, { cls: 'moon' }));
    themeBtn.addEventListener('click', () => setTheme(theme === 'dark' ? 'light' : 'dark'));

    const reload = el('button', 'btn');
    reload.type = 'button';
    reload.title = 'Load the tool again';
    reload.append(svg(IC.reload, { width: 2 }), el('span', '', 'Reload'));
    reload.addEventListener('click', () => location.reload());

    const x = el('button', 'x');
    x.type = 'button';
    x.title = 'Close and go back to Canvas';
    x.setAttribute('aria-label', 'Close and go back to Canvas');
    x.append(svg(IC.close, { size: 13, width: 2.3 }));
    x.addEventListener('click', () => { try { api.runtime.sendMessage({ type: 'closeTool' }); } catch { window.close(); } });

    const hide = el('button', 'btn icon hide');
    hide.type = 'button';
    hide.title = 'Hide this bar';
    hide.setAttribute('aria-label', 'Hide this bar');
    hide.append(svg(IC.up, { size: 15, width: 2.2 }));
    hide.addEventListener('click', () => setHidden(true));

    const acts = el('div', 'acts');
    acts.append(themeBtn, reload, hide);
    const bar = el('div', 'bar');
    bar.append(tile, titles, acts, x);

    // what is left of the bar when it is tucked away: a small tab at the top of the page
    const peek = el('button', 'peek');
    peek.type = 'button';
    peek.title = 'Show the Simpl bar';
    peek.setAttribute('aria-label', 'Show the Simpl bar');
    peek.append(svg(IC.down, { size: 14, width: 2.4 }));
    peek.addEventListener('click', () => setHidden(false));

    const auth = el('div', 'auth');
    auth.append(svg(IC.warn, { size: 20, width: 2 }), el('span', '', 'Authenticating. Don’t open any new tabs or windows'));

    // the state is on the host before the bar is first laid out (attach measures it), so the bar's
    // first paint is already its colour for that state rather than a transition into it; the same
    // for a bar the last tool left tucked away, which arrives tucked rather than sliding up
    host.dataset.state = tool.state === 'auth' ? 'auth' : 'ready';
    if (hidden) host.dataset.hidden = '';
    root.append(style, bar, peek, auth);
    attach();
    paint();
    setState(tool.state);
    watchRoom();
    widgets();
  }

  /** Keep the bar on the page: a tool that rewrites the document from scratch would take it with it. */
  function attach() {
    if (!host) return;
    if (host.parentNode !== document.documentElement) document.documentElement.append(host);
    push();
  }

  // Asking once is asking too early. This runs before the page paints, and the background may not
  // have written the session down yet — the tab it just made, or the one it is about to adopt from
  // whatever opened it. So it asks again for a couple of seconds and then gives up quietly, which
  // is what every page that is not a tool's does. The background can ask it to try again (toolPing,
  // after a move of the tab it knows to be a tool's), so a copy that gave up is not the end of it.
  let asking = false;
  async function wake() {
    if (host || asking) return;
    asking = true;
    let tool = null;
    try {
      for (const ms of [0, 120, 300, 700, 1400, 2500]) {
        if (ms) await new Promise((r) => setTimeout(r, ms));
        try { tool = (await api.runtime.sendMessage({ type: 'toolTab' }))?.tool || null; } catch { return; } // no background to ask
        if (tool) break;
      }
      if (!tool) return;
      try {
        const r = await api.storage.local.get([THEME_KEY, HIDE_KEY]);
        if (r?.[THEME_KEY]) theme = r[THEME_KEY] === 'light' ? 'light' : 'dark';
        hidden = r?.[HIDE_KEY] === true;
      } catch { /* it stays dark, and shown */ }
      // the other tool tabs follow a choice made in one of them
      try { api.storage.onChanged.addListener((ch, area) => { if (area === 'local' && ch[HIDE_KEY] && !!ch[HIDE_KEY].newValue !== hidden) setHidden(!!ch[HIDE_KEY].newValue, { save: false }); }); } catch { /* each tab keeps its own */ }
      const start = () => { if (!host) build(tool); else attach(); paint(); };
      if (document.documentElement) start();
      document.addEventListener('DOMContentLoaded', start);
      window.addEventListener('load', start);
      // A tool that rewrites its document takes the bar with it — at any time, not only while it
      // loads (an app that redraws its whole page on a move). The root's own children are watched,
      // nothing deeper, and the bar is put back the moment it goes; a root replaced whole (a
      // document written over) is caught on a slow beat, which also renews the watch.
      let watched = null;
      const guard = () => {
        const rootEl = document.documentElement;
        if (!rootEl) return;
        if (watched !== rootEl) {
          try { new MutationObserver(() => { if (host && !host.isConnected) attach(); }).observe(rootEl, { childList: true }); watched = rootEl; } catch { /* the beat below stands in */ }
        }
        if (host && !host.isConnected) attach();
      };
      guard();
      setInterval(guard, 3000);
    } finally {
      asking = false;
    }
  }
  try {
    api.runtime.onMessage.addListener((msg, sender, sendResponse) => {
      if (msg?.type === 'toolState') { setState(msg.state); return false; }
      if (msg?.type === 'toolPing') { // the background, after a move: is the bar here? (and if not, ask again)
        sendResponse({ ok: true, bar: !!host });
        if (!host) wake();
        return false;
      }
      return false;
    });
  } catch { /* nothing to hear from: the bar is still built once, below */ }
  wake();
})();
