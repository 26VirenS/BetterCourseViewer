/* The interface's bar over a tool's own page.
 *
 * An external tool is not framed any more. Canvas hands over an address, the tool opens in a tab of
 * its own, and this puts the bar across the top of it: the tool's name, where it is, the look
 * switch, Reload, and the X — which closes the tab and goes back to the Canvas tab it came from.
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
  const IC = {
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
  background: #1c1c1e; color: #f2f2f7; border-bottom: 1px solid rgba(255,255,255,.1); box-shadow: 0 1px 12px rgba(0,0,0,.35); transition: background .18s ease; }
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

  /** Push the page down by whatever the bar and its notice take up, so nothing sits under them. */
  function push() {
    try {
      document.documentElement.style.setProperty('margin-top', `${H + authH}px`, 'important');
      document.documentElement.style.setProperty('--bcv-toolbar-h', `${H + authH}px`); // (what the tray's own popups start below)
      // where the bar's own buttons begin, from its right edge: the tray (the pinned tools) sits just left of them, whatever the window's width
      const bar = root?.querySelector('.bar'), acts = root?.querySelector('.acts'), x = root?.querySelector('.x');
      if (bar && acts && x) {
        const left = Math.min(acts.getBoundingClientRect().left || Infinity, x.getBoundingClientRect().left || Infinity);
        if (Number.isFinite(left)) document.documentElement.style.setProperty('--bcv-toolbar-right', `${Math.round(bar.getBoundingClientRect().right - left + 14)}px`);
      }
    } catch { /* the page went */ }
  }
  try { window.addEventListener('resize', () => { if (host) push(); }); } catch { /* no window to watch */ }

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

    const acts = el('div', 'acts');
    acts.append(themeBtn, reload);
    const bar = el('div', 'bar');
    bar.append(tile, titles, acts, x);

    const auth = el('div', 'auth');
    auth.append(svg(IC.warn, { size: 20, width: 2 }), el('span', '', 'Authenticating. Don’t open any new tabs or windows'));

    // the state is on the host before the bar is first laid out (attach measures it), so the bar's
    // first paint is already its colour for that state rather than a transition into it
    host.dataset.state = tool.state === 'auth' ? 'auth' : 'ready';
    root.append(style, bar, auth);
    attach();
    paint();
    setState(tool.state);
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
      try { const r = await api.storage.local.get(THEME_KEY); if (r?.[THEME_KEY]) theme = r[THEME_KEY] === 'light' ? 'light' : 'dark'; } catch { /* it stays dark */ }
      const start = () => { if (!host) build(tool); else attach(); paint(); };
      if (document.documentElement) start();
      document.addEventListener('DOMContentLoaded', start);
      window.addEventListener('load', start);
      const t = setInterval(attach, 1000);
      setTimeout(() => clearInterval(t), 15000);
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
