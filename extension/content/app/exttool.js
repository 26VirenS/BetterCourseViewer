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

  /** Open a tool in a tab of its own, with the bar over it. */
  function open({ title = 'External tool', url, page = null, newTab = null, note = '', from = null, icon = null } = {}) {
    if (!url) return null;
    const target = launched(url);
    if (from) try { from.blur?.(); } catch { /* it went */ }
    try {
      BCV.api.runtime.sendMessage({ type: 'openTool', url: target, title, note });
    } catch {
      window.open(newTab || page || url, '_blank', 'noopener'); // no background to ask: the browser's own new tab, bare
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
    close: () => {}, isOpen: () => false, // (nothing is framed over the page any more)
    theme: { get: () => theme, set: setTheme, button: themeButton },
  };
})();
