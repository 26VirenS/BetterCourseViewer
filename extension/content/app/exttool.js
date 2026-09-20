/* External tools in a popup over the page. A course's Campus tools, a module's external tool or
 * link, an assignment's tool, the school's own nav tools: Canvas draws them on a page of their own
 * or sends them to a new tab. Here they open in a popup that fills the tab — the tool framed inside
 * it, through Canvas's own borderless launch for the tools Canvas launches, the site itself for a
 * plain link — so the page underneath and the pinned tools beside the switch stay where they are.
 * Open in new tab is always in the head, for a site that refuses to be framed; Open in Canvas goes
 * to Canvas's own page for a Canvas tool. Escape and the scrim close it. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;

  let current = null;
  const isOpen = () => !!current && current.ov.isConnected;
  function close() {
    const c = current;
    current = null;
    if (!c) return;
    document.removeEventListener('securitypolicyviolation', c.onCsp);
    c.ov.classList.add('is-closing');
    setTimeout(() => c.ov.remove(), 180);
    try { c.restore?.focus?.({ preventScroll: true }); } catch { /* gone */ }
  }
  /** Canvas's launch page for one of its tools, without Canvas's chrome round it. */
  function borderless(href) {
    const u = new URL(href, location.origin);
    if (u.origin === location.origin && /\/external_tools\//.test(u.pathname) && !u.searchParams.has('display')) u.searchParams.set('display', 'borderless');
    return u.href;
  }
  const withNative = (path) => `${path}${path.includes('?') ? '&' : '?'}bcv=native`;
  const sameOrigin = (href) => { try { return new URL(href, location.origin).origin === location.origin; } catch { return false; } };

  /** The popup. `url` is what gets framed; `page` Canvas's own page for it (Open in Canvas), if
   *  there is one; `newTab` what a new tab gets (the page, else the url). */
  function open({ title = 'External tool', url, page = null, newTab = null, note = '', from = null, icon = null } = {}) {
    if (!url) return null;
    close();
    const tabUrl = newTab || page || url;
    const foreign = !sameOrigin(url);
    const ov = U.el('bcv-sheet-ov bcv-ext-ov', null, { role: 'dialog', 'aria-label': title, tabindex: '-1' });
    const frame = h('iframe', { class: 'bcv-ext__frame', src: url, title, allow: 'fullscreen; microphone; camera; display-capture; autoplay; clipboard-write; geolocation', referrerpolicy: 'strict-origin-when-cross-origin' });
    const wait = U.el('bcv-ext__wait', [U.text('bcv-ext__waittext', foreign ? 'Opening… if it stays blank, the site does not allow this: open it in a new tab.' : 'Opening…')]);
    const body = U.el('bcv-ext__body', [wait, frame]);
    const fail = () => {
      if (current?.ov !== ov) return;
      body.replaceChildren(U.el('bcv-ext__fail', [
        U.svg(IC.warn, { size: 26, stroke: 'var(--bcv-orange)', width: 1.9 }),
        U.text('bcv-ext__failtitle', 'This one will not open in a popup'),
        U.text('bcv-ext__failtext bcv-pretty', 'Your school’s Canvas does not allow it to be framed. Open it in a new tab instead.'),
        h('a', { class: 'bcv-btn bcv-btn--primary', href: tabUrl, target: '_blank', rel: 'noopener', text: 'Open in new tab' }),
      ]));
    };
    const onCsp = (e) => { const b = String(e.blockedURI || ''); if (b && (url.startsWith(b) || b.startsWith(url.slice(0, 40)))) fail(); };
    document.addEventListener('securitypolicyviolation', onCsp);
    frame.addEventListener('load', () => { frame.classList.add('is-in'); body.classList.add('is-loaded'); });
    frame.addEventListener('error', fail);
    const closeBtn = h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 }));
    const tab = h('a', { class: 'bcv-btn bcv-ext__tab', href: tabUrl, target: '_blank', rel: 'noopener', title: 'Open in a new tab' }, [U.svg(IC.external, { size: 13, stroke: 'currentColor', width: 1.9 }), h('span', { text: 'Open in new tab' })]);
    const head = U.el('bcv-sheet__head bcv-ext__head', [
      U.tile(icon || IC.shield, { color: 'var(--bcv-blue)', tint: 'var(--bcv-blue-soft)', size: 32, iconSize: 16 }),
      U.el('bcv-sheet__titles', [U.text('bcv-sheet__title', title), U.text('bcv-sheet__note', note || (foreign ? 'The site’s own page, framed here. The pinned tools stay beside the switch.' : 'Launched by Canvas, framed here. The pinned tools stay beside the switch.'))]),
      U.el('bcv-ext__acts', [
        page ? U.btn('Open in Canvas', { cls: 'bcv-ext__canvas', onClick: () => { close(); BCV.app?.go(withNative(page)); } }) : null,
        tab,
      ]),
      closeBtn,
    ]);
    const sheet = U.el('bcv-sheet bcv-ext', [head, body]);
    ov.append(sheet);
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') { e.stopPropagation(); close(); } });
    current = { ov, onCsp, restore: from && from.focus ? from : document.activeElement };
    document.body.append(ov);
    if (from) U.morphFrom(sheet, from);
    ov.focus({ preventScroll: true });
    return { close, ov };
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

  BCV.exttool = { open, openLink, close, isOpen, isToolHref, borderless };
})();
