/* Canvas's own global navigation, kept. A school adds things to the left-hand nav that no
 * API lists: account-level tools with a global placement ("My Materials", Studio, a library),
 * History, Help, sometimes an admin link. Every Canvas page carries that menu in its markup
 * (#menu), so it is read from the page and shown under our own navigation: tools and links
 * open Canvas's page for them (the shell stays over it), History and Help open as sheets
 * drawn from the same lists Canvas's trays read. The last menu seen is kept for pages that
 * do not carry one. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  const HELP = 'M12 4a8 8 0 100 16 8 8 0 000-16zM9.6 9.3a2.5 2.5 0 014.9.7c0 1.6-2.5 2-2.5 3.5M12 17h.01';
  const OURS = new Set(['dashboard', 'courses', 'groups', 'calendar', 'conversations', 'profile']);
  const ICONS = { assignment: IC.doc, quiz: IC.bolt, discussion: IC.disc, announcement: IC.bell, document: IC.page, page: IC.page, module: IC.modules, folder: IC.folder, 'calendar-month': IC.cal, home: IC.book, syllabus: IC.page, grades: IC.chart, group: IC.people, user: IC.people };
  let memo = null; // the last menu seen (from this page, else from the preference)

  /** The page's #menu as items we do not already draw. */
  function parseMenu() {
    const items = [];
    for (const li of document.querySelectorAll('#menu > li')) {
      const a = li.querySelector('a, button');
      if (!a) continue;
      const id = (a.id || '').replace(/^global_nav_/, '').replace(/_link$/, '');
      const label = (li.querySelector('.menu-item__text')?.textContent || a.getAttribute('aria-label') || a.textContent || '').replace(/\s+/g, ' ').trim();
      const href = a.getAttribute('href') || '';
      const img = li.querySelector('img');
      if (OURS.has(id)) continue;
      if (id === 'history') items.push({ kind: 'history', label: label || 'History' });
      else if (id === 'help') items.push({ kind: 'help', label: label || 'Help' });
      else if (href && href !== '#' && /external_tools/.test(href)) items.push({ kind: 'tool', label: label || 'Tool', href, icon: img?.getAttribute('src') || null });
      else if (href && href !== '#' && label) items.push({ kind: 'link', label, href });
    }
    return items;
  }

  /** What to show now: this page's menu, else the last one seen. The menu is written down for the
   *  pages without one — once it differs from what is written, not on every page load. */
  let saved; // the preference as last read or written (undefined until read once)
  function items() {
    const live = parseMenu();
    if (live.length) {
      const json = JSON.stringify(live);
      if (JSON.stringify(memo) !== json) {
        memo = live;
        (saved === undefined ? store.pref('globalNav', null).catch(() => null) : Promise.resolve(saved)).then((prev) => {
          saved = prev;
          if (JSON.stringify(prev) !== json) { saved = live; return store.setPref('globalNav', live); }
          return null;
        }).catch(() => {});
      }
      return live;
    }
    return memo || [];
  }

  /** On a page without the menu (rare): read the last one seen, then redraw the shell. */
  async function prime(app) {
    if (parseMenu().length) return;
    const saved = await store.pref('globalNav', null);
    if (Array.isArray(saved) && saved.length && !memo) {
      memo = saved;
      app.renderSide?.();
    }
  }

  function open(app, it) {
    if (it.kind === 'history') historySheet(app);
    else if (it.kind === 'help') helpSheet(app);
    else if (it.kind === 'tool' && BCV.exttool) BCV.exttool.openLink({ title: it.label, href: it.href }); // (a tab of its own, this page left where it is)
    else app.go(it.href);
  }

  // glyphs like our own nav rows (mockup 11); a tool's own icon keeps a small dark tile, as it is drawn for Canvas's dark nav
  const tileIcon = (it) => (it.kind === 'history' ? U.svg(IC.clock, { size: 21, stroke: 'var(--bcv-ink2)', width: 1.8 })
    : it.kind === 'help' ? U.svg(HELP, { size: 21, stroke: 'var(--bcv-ink2)', width: 1.8 })
      : it.icon ? h('img', { src: it.icon, alt: '', referrerpolicy: 'no-referrer' }) : U.svg(IC.external, { size: 14, stroke: '#fff', width: 1.9 }));

  /** The sidebar group under our navigation (desktop). */
  function sideGroup(app) {
    const list = items();
    if (!list.length) return null;
    return U.el('bcv-side__group bcv-side__group--more', [
      U.text('bcv-side__label', 'More from Canvas'),
      ...list.map((it) => h('button', { type: 'button', class: 'bcv-nav__item bcv-nav__item--more', dataset: { extra: it.kind }, title: it.label, onclick: () => open(app, it) }, [
        h('span', { class: `bcv-nav__ic ${it.kind === 'tool' || it.kind === 'link' ? 'bcv-nav__ic--tool' : ''}` }, tileIcon(it)),
        h('span', { class: 'bcv-ellip', text: it.label }),
        it.kind === 'tool' || it.kind === 'link' ? U.svg(IC.external, { size: 11, stroke: 'var(--bcv-ink3)', width: 2, cls: 'bcv-nav__ext' }) : null,
      ])),
    ]);
  }

  /** Rows for the phone's account sheet. */
  function phoneRows(app) {
    return items().map((it) => (it.kind === 'history' ? { icon: IC.clock, label: it.label, note: 'What you opened lately', onSelect: () => historySheet(app) }
      : it.kind === 'help' ? { icon: HELP, label: it.label, note: 'Your school\'s help links', onSelect: () => helpSheet(app) }
        : it.kind === 'tool' && BCV.exttool ? { icon: IC.external, label: it.label, note: 'Opens in a new tab', onSelect: () => BCV.exttool.openLink({ title: it.label, href: it.href }) }
          : { icon: IC.external, label: it.label, note: 'Opens Canvas\'s page for it', href: it.href }));
  }

  // ---- sheets ---------------------------------------------------------------------------------
  function sheet({ label, title, note, rows, empty, foot = '' }) {
    if (BCV.phone?.active()) {
      return BCV.phone.openSheet({ label, title, note: rows.length ? note : `${note ? `${note} ` : ''}${empty}`.trim(), rows: rows.map((r) => ({ icon: r.icon, label: r.title, note: r.sub, onSelect: r.onSelect })) });
    }
    document.querySelector('.bcv-sheet-ov')?.remove();
    const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': label });
    const close = () => ov.remove();
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
    const list = U.el('bcv-sheet__list', rows.length
      ? rows.map((r) => U.row([
        U.tile(r.icon || IC.page, { color: 'var(--bcv-ink2)', tint: 'var(--bcv-fill)', size: 30, iconSize: 14 }),
        U.el('bcv-sheet__body', [U.text('bcv-xrow__t bcv-ellip', r.title), r.sub ? U.text('bcv-xrow__s bcv-ellip', r.sub) : null]),
        U.chev(),
      ], { cls: 'bcv-xrow', onClick: () => { close(); r.onSelect(); } }))
      : [U.text('bcv-xrow__empty', empty)]);
    ov.append(U.el('bcv-sheet', [
      U.el('bcv-sheet__head', [
        h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-sheet__title', title), note ? U.text('bcv-sheet__desc bcv-pretty', note) : null]),
        h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
      ]),
      list,
      foot ? U.text('bcv-sheet__foot bcv-pretty', foot) : null,
    ]));
    document.body.append(ov);
    ov.tabIndex = -1;
    ov.focus();
    return { close };
  }

  const pathOf = (url) => {
    try {
      const u = new URL(url, location.origin);
      return u.origin === location.origin ? u.pathname + u.search + u.hash : u.href;
    } catch {
      return url || '/';
    }
  };

  /** History: Canvas's own recently-visited list, as the History tray shows it. */
  async function historySheet(app) {
    let list;
    try {
      list = await store.history();
    } catch (e) {
      U.toast(`History could not be loaded: ${e.message}`, { error: true });
      return;
    }
    const rows = (list || []).map((it) => ({
      icon: ICONS[String(it.asset_icon || '').replace(/^icon-/, '')] || IC.page,
      title: it.asset_name || 'Untitled',
      sub: [it.context_name, it.asset_readable_category, it.visited_at ? U.fmtRecent(it.visited_at) : null].filter(Boolean).join(' · '),
      onSelect: () => app.go(pathOf(it.visited_url)),
    }));
    sheet({ label: 'History', title: 'History', note: 'What you opened lately, as Canvas recorded it.', rows, empty: 'Nothing visited yet.' });
  }

  /** Help: the school's help links (the Help tray reads the same list). Entries that need Canvas's own form (report a problem) are left out. */
  async function helpSheet(app) {
    let links = [];
    try {
      links = (await store.helpLinks()) || [];
    } catch {
      links = [];
    }
    const rows = links.filter((l) => l.url && !String(l.url).startsWith('#')).map((l) => ({
      icon: IC.external,
      title: l.text || l.url,
      sub: l.subtext || '',
      onSelect: () => app.go(l.url),
    }));
    if (!rows.length) rows.push({ icon: IC.external, title: 'Canvas Student Guide', sub: 'community.canvaslms.com', onSelect: () => app.go('https://community.canvaslms.com/t5/Student-Guide/tkb-p/student') });
    sheet({ label: 'Help', title: 'Help', note: 'Your school\'s help links, as Canvas lists them.', rows, empty: 'No help links.', foot: links.some((l) => String(l.url || '').startsWith('#')) ? 'Reporting a problem uses Canvas\'s own form: open the page in stock Canvas and choose Help.' : '' });
  }

  BCV.extras = { items, prime, sideGroup, phoneRows, historySheet, helpSheet, parseMenu };
})();
