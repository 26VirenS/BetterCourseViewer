/* External tools in a popup over the page. A course's Campus tools, a module's external tool or
 * link, an assignment's tool, the school's own nav tools: Canvas draws them on a page of their own
 * or sends them to a new tab. Here they open in a popup that fills the tab — the tool framed inside
 * it, through Canvas's own borderless launch for the tools Canvas launches, the site itself for a
 * plain link — so the page underneath and the pinned tools beside the switch stay where they are.
 * It fills the screen but for its bar: the title, Reload, Open in new tab, and — sliding in from
 * where they sit over the page — the pinned tools and the look switch, with the X always at the far
 * right. Escape closes it.
 * Nothing here gives up on a tool. One that is slow to arrive is waited for; the two buttons in the
 * bar are how anyone who would rather not wait gets on with it. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;

  // A popup can open over a popup: a tool that leads to another tool puts it on top rather than in
  // its place, and closing the top one gives back the one underneath, still loaded and where it was.
  const stack = [];
  const top = () => stack[stack.length - 1] || null;
  const isOpen = () => stack.length > 0;
  function close() { closeOne(top()); } // (the one in front: the ones under it are out of reach)
  function closeOne(entry) {
    const i = entry ? stack.indexOf(entry) : -1;
    if (i < 0) return;
    stack.splice(i, 1);
    if (entry.token) tokens.delete(entry.token);
    entry.ov.classList.add('is-closing');
    setTimeout(() => entry.ov.remove(), 180);
    const under = top();
    if (under) { under.ov.classList.remove('is-under'); under.ov.focus({ preventScroll: true }); return; }
    tellBackground(false); // (the last one out: a new tab is the browser's business again)
    document.documentElement.classList.remove('bcv-ext-open'); // (the pins and the look switch slide back out of the bar)
    try { entry.restore?.focus?.({ preventScroll: true }); } catch { /* gone */ }
  }

  // ---- the appearance of a framed popup ------------------------------------------------------
  // A popup that frames somebody else's page is dark unless the sun in its bar says otherwise: the
  // bar takes the dark palette and the page inside is turned over, which is the only way in — the
  // frame is another origin and none of its styling is ours to set. An inversion is not their own
  // dark theme, so the hue goes back round with it and colours land near where they started.
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
  /** The sun (press for light) or the moon (press for dark), for the bar of anything framed. It
   *  names itself on the way out, since a popup built elsewhere never goes through paintTheme. */
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
  // ---- a window the framed tool opens for itself --------------------------------------------
  // Two ways, because neither reaches everywhere. content/popout.js runs inside the frame and takes
  // window.open before the browser makes anything, which is the one that leaves no tab behind — it
  // needs leave to run on the tool's own site, which the builds that may look at every site have.
  // Failing that the background sees the tab afterwards and hands the address back, which works
  // wherever the browser will say a tab was opened, at the cost of the tab being there first.
  const HELLO = 'bcv:popout:hello';
  const ASK = 'bcv:popout:ask';
  const OPENING = 'bcv:popout:opening';
  const tellBackground = (open) => { try { BCV.api?.runtime?.sendMessage?.({ type: 'framedPopup', open }); } catch { /* no background to tell */ } };
  /** Open what a tool asked for, over the popup it asked from. */
  function fromTool(url) {
    let title = 'Opened by the tool';
    try { title = new URL(url).hostname.replace(/^www\./, ''); } catch { /* keep the plain words */ }
    open({ title, url, newTab: url, icon: IC.external });
  }
  /** While a tool's own window is out there signing in, the bar says so and asks for quiet. */
  function busy(on) {
    const t = top();
    if (!t) return;
    t.ov.classList.toggle('is-busy', !!on);
    const bar = t.ov.querySelector('.bcv-ext__busy');
    if (bar) bar.hidden = !on;
  }
  // The hello carries a token and comes back on anything said, which is what stands in for knowing
  // which frame a message came from: a page we never greeted cannot say it. One token per popup,
  // and a token is only good while its popup is open.
  const tokens = new Set();
  window.addEventListener('message', (e) => {
    // a frame asking to be armed — the one that loaded after the hellos stopped
    if (e.data?.type === ASK && e.source && e.source !== window) {
      const t = top();
      if (t?.token) { try { e.source.postMessage({ type: HELLO, token: t.token }, '*'); } catch { /* it will ask again */ } }
      return;
    }
    if (e.data?.type !== OPENING || !tokens.has(e.data.token) || !isOpen()) return;
    busy(true); // (the background turns it off again once the window has settled, or gone)
  });
  /** Greet the frame, again as it loads: a tool that navigates itself has a new document each time. */
  function greet(frame, token) {
    tokens.add(token);
    const say = () => { try { frame.contentWindow?.postMessage({ type: HELLO, token }, '*'); } catch { /* not there yet */ } };
    say();
    for (const ms of [150, 600, 1500, 3000]) setTimeout(say, ms);
    frame.addEventListener('load', say);
  }
  /** Our own way to a tab, said out loud: without this the catcher would take it straight back. */
  const allowTab = () => { try { BCV.api?.runtime?.sendMessage?.({ type: 'framedAllowTab' }); } catch { /* nothing to tell */ } };
  try {
    BCV.api?.runtime?.onMessage?.addListener?.((msg) => {
      if (msg?.type === 'framedBusy') { busy(!!msg.on); return undefined; } // signing in out there, or done
      if (msg?.type !== 'framedPopup' || !msg.url) return undefined;
      if (msg.toast) U.toast(`Caught a tab: ${msg.url}`, { ms: 4000 }); // (the Developer section's own running commentary)
      if (!isOpen()) return undefined; // (nothing to put it over: the browser keeps its window)
      fromTool(msg.url);
      return undefined;
    });
  } catch { /* no background to hear from */ }

  /** Canvas's launch page for one of its tools, without Canvas's chrome round it. */
  function borderless(href) {
    const u = new URL(href, location.origin);
    if (u.origin === location.origin && /\/external_tools\//.test(u.pathname) && !u.searchParams.has('display')) u.searchParams.set('display', 'borderless');
    return u.href;
  }

  /** The popup. `url` is what gets framed; `newTab` what a new tab gets (else `page`, Canvas's own page for it, else the url). */
  function open({ title = 'External tool', url, page = null, newTab = null, note = '', from = null, icon = null } = {}) {
    if (!url) return null;
    const under = top();
    if (under) under.ov.classList.add('is-under'); // (it waits, loaded, out of reach until this one closes)
    let entry = null;
    const alive = () => stack.includes(entry);
    const tabUrl = newTab || page || url;
    const ov = U.el('bcv-sheet-ov bcv-ext-ov', null, { role: 'dialog', 'aria-label': title, tabindex: '-1' });
    const frame = h('iframe', { class: 'bcv-ext__frame', src: url, title, allow: 'fullscreen; microphone; camera; display-capture; autoplay; clipboard-write; geolocation; publickey-credentials-get; identity-credentials-get', referrerpolicy: 'strict-origin-when-cross-origin' }); // (no sandbox: a tool signs in, sets its cookies and opens its windows as it would on Canvas's own page)
    const wait = U.el('bcv-ext__wait', [U.text('bcv-ext__waittext', 'Opening…')]);
    const body = U.el('bcv-ext__body', [wait, frame]);
    // Nothing here decides a tool has failed. It used to: a tool still blank after a while was given
    // a tab of its own, and one whose site refused the frame got a card saying so. Both took the
    // choice away — a slow tool is slow, not lost — so the popup waits, and Open in new tab and
    // Reload in the bar are there for whoever wants them.
    frame.addEventListener('load', () => {
      if (frame.getAttribute('src') === 'about:blank') return; // (the blank page on the way through a reload: the tool is still to come)
      frame.classList.add('is-in');
      body.classList.add('is-loaded');
    });
    const closeBtn = h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: () => closeOne(entry) }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 }));
    const tab = h('a', { class: 'bcv-btn bcv-ext__tab', href: tabUrl, target: '_blank', rel: 'noopener', title: 'Open in a new tab', onclick: allowTab }, [U.svg(IC.external, { size: 13, stroke: 'currentColor', width: 1.9 }), h('span', { text: 'Open in new tab' })]);
    // Reload: the launch again from the start (a tool that timed out, a sign-in that went round in circles)
    const reload = h('button', { type: 'button', class: 'bcv-btn bcv-ext__reload', title: 'Load the tool again', 'aria-label': 'Reload' }, [U.svg('M4 12a8 8 0 108-8M4 4v5h5', { size: 13, stroke: 'currentColor', width: 2 }), h('span', { text: 'Reload' })]);
    reload.addEventListener('click', () => {
      frame.classList.remove('is-in');
      body.classList.remove('is-loaded'); // "Opening…" again until the tool is back
      frame.src = 'about:blank'; // through a blank page, so the launch starts over rather than the browser answering from what it had
      setTimeout(() => { if (alive()) frame.src = url; }, 30);
    });
    // shown while a window the tool opened is out there: the bar goes dark red and says to leave it be
    const busyBar = U.el('bcv-ext__busy', [
      U.svg(IC.warn, { size: 20, stroke: 'currentColor', width: 2 }),
      U.text('bcv-ext__busytext', 'Authenticating. Don’t open any new tabs or windows'),
    ], { hidden: true });
    const head = U.el('bcv-sheet__head bcv-ext__head', [
      U.tile(icon || IC.shield, { color: 'var(--bcv-blue)', tint: 'var(--bcv-blue-soft)', size: 32, iconSize: 16 }),
      U.el('bcv-sheet__titles', [U.text('bcv-sheet__title', title), note ? U.text('bcv-sheet__note', note) : null]), // (the bar carries the tool's name and nothing more)
      U.el('bcv-ext__acts', [themeButton(), reload, tab]),
      closeBtn,
    ]);
    closeBtn.classList.add('bcv-ext__close');
    const sheet = U.el('bcv-sheet bcv-ext', [head, busyBar, body]);
    ov.append(sheet);
    ov.addEventListener('click', (e) => { if (e.target === ov) closeOne(entry); });
    ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') { e.stopPropagation(); closeOne(entry); } }); // (only the one in front has the focus, so a stack comes apart one at a time)
    entry = { ov, restore: under ? null : (from && from.focus ? from : document.activeElement) };
    stack.push(entry);
    entry.token = `bcv${Math.random().toString(36).slice(2)}${Date.now().toString(36)}`;
    greet(frame, entry.token); // (the frame's own window.open is taken over from here: no tab is made at all)
    tellBackground(true); // (and where that cannot reach, the background catches the tab instead)
    document.body.append(ov);
    paintTheme(); // (the sun or the moon in this bar, named for what pressing it does)
    document.documentElement.classList.add('bcv-ext-open'); // the pins and the look switch slide into the bar, beside the X
    if (from) U.morphFrom(sheet, from);
    ov.focus({ preventScroll: true });
    return { close: () => closeOne(entry), ov };
  }
  /** A link to a tool, as Canvas gives it: Canvas's own tool pages are framed borderless with Open in
   *  Canvas beside them; any other address is framed as it is. */
  function openLink({ title, href, from = null, icon = null, note = '' } = {}) {
    let u;
    try { u = new URL(href, location.origin); } catch { return null; }
    const own = u.origin === location.origin;
    return open({ title, url: own ? borderless(u.href) : u.href, page: own ? u.pathname + u.search : null, newTab: u.href, from, icon, note });
  }
  /** Is this address one Canvas launches a tool from? */
  const isToolHref = (href) => { try { const u = new URL(href, location.origin); return u.origin === location.origin && /\/external_tools\/(\d+|retrieve)\b/.test(u.pathname) && !u.searchParams.has('bcv'); } catch { return false; } };

  BCV.exttool = { open, openLink, close, isOpen, isToolHref, borderless, theme: { get: () => theme, set: setTheme, button: themeButton } };
})();
