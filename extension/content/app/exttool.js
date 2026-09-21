/* External tools in a popup over the page. A course's Campus tools, a module's external tool or
 * link, an assignment's tool, the school's own nav tools: Canvas draws them on a page of their own
 * or sends them to a new tab. Here they open in a popup that fills the tab — the tool framed inside
 * it, through Canvas's own borderless launch for the tools Canvas launches, the site itself for a
 * plain link — so the page underneath and the pinned tools beside the switch stay where they are.
 * It fills the screen but for its bar: the title, Open in new tab (for a site that refuses to be
 * framed), and — sliding in from where they sit over the page — the pinned tools and the look
 * switch, with the X always at the far right. Escape closes it. */
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
    entry.stopWaiting?.();
    document.removeEventListener('securitypolicyviolation', entry.onCsp);
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
  // The frame is another origin, so its window.open cannot be caught here. The background sees it
  // as a new tab opened by this one and hands the address back (see catchOpenedTab there); it goes
  // into a popup over the one already up, which is where the tool meant to put it.
  const tellBackground = (open) => { try { BCV.api?.runtime?.sendMessage?.({ type: 'framedPopup', open }); } catch { /* no background to tell */ } };
  /** Our own way to a tab, said out loud: without this the catcher would take it straight back. */
  const allowTab = () => { try { BCV.api?.runtime?.sendMessage?.({ type: 'framedAllowTab' }); } catch { /* nothing to tell */ } };
  try {
    BCV.api?.runtime?.onMessage?.addListener?.((msg) => {
      if (msg?.type !== 'framedPopup' || !msg.url || !isOpen()) return undefined;
      let title = 'Opened by the tool';
      try { title = new URL(msg.url).hostname.replace(/^www\./, ''); } catch { /* keep the plain words */ }
      open({ title, url: msg.url, newTab: msg.url, icon: IC.external });
      return undefined;
    });
  } catch { /* no background to hear from */ }

  /** Canvas's launch page for one of its tools, without Canvas's chrome round it. */
  function borderless(href) {
    const u = new URL(href, location.origin);
    if (u.origin === location.origin && /\/external_tools\//.test(u.pathname) && !u.searchParams.has('display')) u.searchParams.set('display', 'borderless');
    return u.href;
  }
  const sameOrigin = (href) => { try { return new URL(href, location.origin).origin === location.origin; } catch { return false; } };

  /** The popup. `url` is what gets framed; `newTab` what a new tab gets (else `page`, Canvas's own page for it, else the url). */
  function open({ title = 'External tool', url, page = null, newTab = null, note = '', from = null, icon = null } = {}) {
    if (!url) return null;
    const under = top();
    if (under) under.ov.classList.add('is-under'); // (it waits, loaded, out of reach until this one closes)
    let entry = null;
    const alive = () => stack.includes(entry);
    const tabUrl = newTab || page || url;
    const foreign = !sameOrigin(url);
    const ov = U.el('bcv-sheet-ov bcv-ext-ov', null, { role: 'dialog', 'aria-label': title, tabindex: '-1' });
    const frame = h('iframe', { class: 'bcv-ext__frame', src: url, title, allow: 'fullscreen; microphone; camera; display-capture; autoplay; clipboard-write; geolocation; publickey-credentials-get; identity-credentials-get', referrerpolicy: 'strict-origin-when-cross-origin' }); // (no sandbox: a tool signs in, sets its cookies and opens its windows as it would on Canvas's own page)
    const wait = U.el('bcv-ext__wait', [U.text('bcv-ext__waittext', foreign ? 'Opening… if it stays blank, the site does not allow this: open it in a new tab.' : 'Opening…')]);
    const body = U.el('bcv-ext__body', [wait, frame]);
    // A tool that never arrives: after ten seconds the popup gives up and the tool gets a tab of its
    // own, which is where Canvas would have sent it anyway. (A tab opened this late is not the
    // browser's idea of a press, so the background opens it; window.open is the fallback, and the
    // card below is what is left when the browser refuses both.)
    const SLOW = 10000;
    let late = 0;
    const stopWaiting = () => { clearTimeout(late); late = 0; };
    const fail = () => {
      stopWaiting();
      if (!alive()) return;
      body.replaceChildren(U.el('bcv-ext__fail', [
        U.svg(IC.warn, { size: 26, stroke: 'var(--bcv-orange)', width: 1.9 }),
        U.text('bcv-ext__failtitle', 'This one will not open in a popup'),
        U.text('bcv-ext__failtext bcv-pretty', 'Your school’s Canvas does not allow it to be framed. Open it in a new tab instead.'),
        h('a', { class: 'bcv-btn bcv-btn--primary', href: tabUrl, target: '_blank', rel: 'noopener', text: 'Open in new tab', onclick: allowTab }),
      ]));
    };
    const toTab = async () => {
      if (!alive()) return;
      stopWaiting();
      let opened = false;
      try { opened = !!(await BCV.api?.runtime?.sendMessage?.({ type: 'openTab', url: tabUrl }))?.ok; } catch { opened = false; }
      if (!opened) { allowTab(); opened = !!window.open(tabUrl, '_blank', 'noopener'); }
      if (opened) closeOne(entry);
      else fail();
    };
    const waitOn = () => { stopWaiting(); late = setTimeout(toTab, SLOW); };
    const onCsp = (e) => { const b = String(e.blockedURI || ''); if (b && (url.startsWith(b) || b.startsWith(url.slice(0, 40)))) fail(); };
    document.addEventListener('securitypolicyviolation', onCsp);
    frame.addEventListener('load', () => {
      if (frame.getAttribute('src') === 'about:blank') return; // (the blank page on the way through a reload: the tool is still to come)
      stopWaiting();
      frame.classList.add('is-in');
      body.classList.add('is-loaded');
    });
    frame.addEventListener('error', fail);
    const closeBtn = h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: () => closeOne(entry) }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 }));
    const tab = h('a', { class: 'bcv-btn bcv-ext__tab', href: tabUrl, target: '_blank', rel: 'noopener', title: 'Open in a new tab', onclick: allowTab }, [U.svg(IC.external, { size: 13, stroke: 'currentColor', width: 1.9 }), h('span', { text: 'Open in new tab' })]);
    // Reload: the launch again from the start (a tool that timed out, a sign-in that went round in circles)
    const reload = h('button', { type: 'button', class: 'bcv-btn bcv-ext__reload', title: 'Load the tool again', 'aria-label': 'Reload' }, [U.svg('M4 12a8 8 0 108-8M4 4v5h5', { size: 13, stroke: 'currentColor', width: 2 }), h('span', { text: 'Reload' })]);
    reload.addEventListener('click', () => {
      frame.classList.remove('is-in');
      body.classList.remove('is-loaded'); // "Opening…" again until the tool is back
      frame.src = 'about:blank'; // through a blank page, so the launch starts over rather than the browser answering from what it had
      setTimeout(() => { if (alive()) frame.src = url; }, 30);
      waitOn(); // (the second try gets the same five seconds)
    });
    const head = U.el('bcv-sheet__head bcv-ext__head', [
      U.tile(icon || IC.shield, { color: 'var(--bcv-blue)', tint: 'var(--bcv-blue-soft)', size: 32, iconSize: 16 }),
      U.el('bcv-sheet__titles', [U.text('bcv-sheet__title', title), note ? U.text('bcv-sheet__note', note) : null]), // (the bar carries the tool's name and nothing more)
      U.el('bcv-ext__acts', [themeButton(), reload, tab]),
      closeBtn,
    ]);
    closeBtn.classList.add('bcv-ext__close');
    const sheet = U.el('bcv-sheet bcv-ext', [head, body]);
    ov.append(sheet);
    ov.addEventListener('click', (e) => { if (e.target === ov) closeOne(entry); });
    ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') { e.stopPropagation(); closeOne(entry); } }); // (only the one in front has the focus, so a stack comes apart one at a time)
    entry = { ov, onCsp, stopWaiting, restore: under ? null : (from && from.focus ? from : document.activeElement) };
    stack.push(entry);
    tellBackground(true); // (from here a window the tool opens for itself is caught and brought back)
    waitOn();
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
