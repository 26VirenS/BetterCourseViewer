/* Search everything: a box in the Dashboard's header that searches all of Canvas — the courses, and
 * for each starred course its assignments, announcements, pages, discussions and files — and people,
 * and Wikipedia, from one field. Results come in groups under the box as you type (after a short
 * pause, two characters or more), each group as its source answers; the arrows walk them, Enter
 * opens the one chosen (the first, when none is), Escape closes. A Canvas item opens in place; a
 * Wikipedia article opens in a new tab. "/" (or ⌘K / Ctrl+K) puts the cursor in the box from
 * anywhere on the Dashboard. The Wikipedia lookup goes through the background (background.js
 * 'wiki'), so the page's own rules never block it. The first time, a black screen points at the
 * box (welcome.js: "Search Everything."). */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;
  const C = BCV.canvas;
  const MIN = 2; // characters before a search
  const PAUSE = 280; // ms of quiet after the last keystroke
  const PER = 5; // results a group shows at most

  let ui = null; // the box on the page: { app, root, input, panel, seq, q, groups, pending, cursor, items, timer }
  const norm = (s) => String(s || '').toLowerCase().trim();
  const hit = (s, q) => norm(s).includes(q);
  const nameOf = (c) => c.nickname || c.code || c.name;
  const when = (iso) => { try { return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }); } catch { return ''; } };
  const favs = async () => { try { return (await store.favorites() || []).filter((c) => c.state !== 'past'); } catch { return []; } }; // (the starred courses as the Dashboard has them; every current course when none is starred)
  const LANES = 4; // requests in flight at once across the starred courses (a burst of forty at Canvas trips its rate limit)
  /** A queue that runs `n` jobs at a time. */
  const limiter = (n) => { let busy = 0; const queue = []; const next = () => { if (busy >= n || !queue.length) return; busy += 1; queue.shift()().finally(() => { busy -= 1; next(); }); }; return (fn) => new Promise((resolve, reject) => { queue.push(() => Promise.resolve().then(fn).then(resolve, reject)); next(); }); };

  // ---- the sources: each resolves to a group's items (PER at most), or nothing ----------------------
  async function courseHits(q) {
    const cs = await store.courses().catch(() => []);
    return cs.filter((c) => c.state === 'current' && [c.name, c.code, c.nickname].some((s) => hit(s, q))).slice(0, PER)
      .map((c) => ({ icon: IC.book, title: nameOf(c), sub: c.name !== nameOf(c) ? c.name : 'Course', href: `/courses/${c.id}`, tint: c.color || null }));
  }
  /** One of a course's lists, for every starred course: Canvas narrows it by search_term where it can, and the title is checked here in any case. */
  const perCourse = (q, cs, lane, path, params, pick) => Promise.all(cs.map((c) => lane(async () => {
    try {
      const rows = await C.get(`/api/v1/courses/${c.id}${path}`, { params: { search_term: q, per_page: 20, ...params } });
      return (Array.isArray(rows) ? rows : []).map((r) => pick(r, c)).filter((it) => it && it.title && (hit(it.title, q) || (it.alt && hit(it.alt, q))));
    } catch { return []; } // (a list the course keeps from students — files, often — is no result, not an error)
  }))).then((lists) => lists.flat().slice(0, PER));
  const assignmentHits = (q, cs, lane) => perCourse(q, cs, lane, '/assignments', {}, (a, c) => ({ icon: IC.doc, title: a.name, sub: `${nameOf(c)} · ${a.due_at ? `Due ${when(a.due_at)}` : 'Assignment'}`, href: `/courses/${c.id}/assignments/${a.id}` }));
  const announcementHits = (q, cs, lane) => perCourse(q, cs, lane, '/discussion_topics', { only_announcements: true }, (t, c) => ({ icon: IC.bell, title: t.title, sub: `${nameOf(c)} · Announcement`, href: `/courses/${c.id}/discussion_topics/${t.id}` }));
  const pageHits = (q, cs, lane) => perCourse(q, cs, lane, '/pages', {}, (p, c) => ({ icon: IC.page, title: p.title, sub: `${nameOf(c)} · Page`, href: `/courses/${c.id}/pages/${p.url}` }));
  const discussionHits = (q, cs, lane) => perCourse(q, cs, lane, '/discussion_topics', {}, (t, c) => ({ icon: IC.people, title: t.title, sub: `${nameOf(c)} · Discussion`, href: `/courses/${c.id}/discussion_topics/${t.id}` }));
  const fileHits = (q, cs, lane) => perCourse(q, cs, lane, '/files', {}, (f, c) => ({ icon: IC.folder, title: f.display_name || f.filename, alt: f.filename, sub: `${nameOf(c)} · File`, href: `/courses/${c.id}/files/${f.id}` }));
  async function peopleHits(q) {
    try {
      const rows = await C.get('/api/v1/search/recipients', { params: { search: q, per_page: 10 } });
      return (Array.isArray(rows) ? rows : []).filter((r) => r && r.name && !/^(course|group|section)_/.test(String(r.id)) && hit(r.name, q)).slice(0, PER)
        .map((r) => { const cid = Object.keys(r.common_courses || {})[0]; return { icon: IC.people, title: r.name, sub: 'Person', href: cid ? `/courses/${cid}/users/${r.id}` : `/users/${r.id}` }; });
    } catch { return []; }
  }
  async function wikiHits(q) {
    if (!ui?.wiki) return []; // (the W beside the box, off)
    try {
      const r = await Promise.resolve(BCV.api.runtime.sendMessage({ type: 'wiki', q }));
      return r?.ok ? (r.hits || []).slice(0, PER).map((w) => ({ icon: IC.globe, title: w.title, sub: w.text || 'Wikipedia', url: w.url })) : [];
    } catch { return []; }
  }
  const GROUPS = [
    ['Courses', (q) => courseHits(q)],
    ['Assignments', (q, cs, lane) => assignmentHits(q, cs, lane)],
    ['Announcements', (q, cs, lane) => announcementHits(q, cs, lane)],
    ['Pages', (q, cs, lane) => pageHits(q, cs, lane)],
    ['Discussions', (q, cs, lane) => discussionHits(q, cs, lane)],
    ['Files', (q, cs, lane) => fileHits(q, cs, lane)],
    ['People', (q) => peopleHits(q)],
    ['Wikipedia', (q) => wikiHits(q)],
  ];

  // ---- a search: every source asked at once, each group painted as it answers ---------------------
  async function run(q) {
    if (!ui) return;
    const seq = ++ui.seq;
    ui.q = q; ui.groups = new Map(); ui.pending = GROUPS.length; ui.cursor = -1;
    paint();
    const cs = await favs();
    if (!ui || ui.seq !== seq) return;
    const lane = limiter(LANES);
    for (const [name, fn] of GROUPS) {
      Promise.resolve().then(() => fn(q, cs, lane)).catch(() => []).then((items) => {
        if (!ui || ui.seq !== seq) return;
        ui.groups.set(name, items || []);
        ui.pending -= 1;
        paint();
      });
    }
  }
  function paint() {
    if (!ui) return;
    const items = [];
    const blocks = [];
    for (const [name] of GROUPS) {
      const list = ui.groups.get(name);
      if (!list || !list.length) continue;
      const rows = list.map((it) => {
        items.push(it);
        return h('button', { type: 'button', class: 'bcv-omni__item', role: 'option', dataset: { i: items.length - 1 }, onclick: () => openItem(it) }, [
          h('span', { class: 'bcv-omni__iic', style: it.tint ? { color: it.tint } : null }, U.svg(it.icon, { size: 15, width: 1.9 })),
          h('span', { class: 'bcv-omni__body' }, [h('span', { class: 'bcv-omni__t', text: it.title }), it.sub ? h('span', { class: 'bcv-omni__s', text: it.sub }) : null]),
          it.url ? h('span', { class: 'bcv-omni__ext', title: 'Opens in a new tab' }, U.svg(IC.external, { size: 12, width: 2 })) : null,
        ]);
      });
      blocks.push(h('div', { class: 'bcv-omni__group', dataset: { group: name } }, [h('div', { class: 'bcv-omni__gtitle', text: name }), ...rows]));
    }
    ui.items = items;
    ui.cursor = Math.min(ui.cursor, items.length - 1);
    if (!items.length) blocks.push(h('div', { class: 'bcv-omni__empty', text: ui.pending ? 'Searching…' : `Nothing for “${ui.input.value.trim()}”` }));
    else if (ui.pending) blocks.push(h('div', { class: 'bcv-omni__more', text: 'Searching…' }));
    ui.panel.replaceChildren(...blocks);
    ui.panel.hidden = false;
    markCursor();
  }
  function markCursor() {
    if (!ui) return;
    ui.panel.querySelectorAll('.bcv-omni__item').forEach((el, i) => el.classList.toggle('is-cur', i === ui.cursor));
    const cur = ui.panel.querySelector('.bcv-omni__item.is-cur');
    if (cur) cur.scrollIntoView({ block: 'nearest' });
  }
  function close() {
    if (!ui) return;
    ui.panel.hidden = true;
    ui.cursor = -1;
    ui.seq += 1; // (a source still answering is answering a search that is closed: it paints nothing)
    ui.pending = 0;
  }
  /** A Canvas item opens in place; a Wikipedia article in a new tab. */
  function openItem(it) {
    if (!it || !ui) return;
    const { app } = ui;
    close();
    if (it.url) window.open(it.url, '_blank', 'noopener');
    else app.go(it.href);
  }
  function onKey(e) {
    if (!ui) return;
    if (e.key === 'Escape') {
      if (!ui.panel.hidden) { close(); e.preventDefault(); e.stopPropagation(); } // (the panel goes, the words stay: a search field would clear itself)
      else ui.input.blur();
      return;
    }
    const q = norm(ui.input.value);
    if (ui.panel.hidden || !ui.items.length) {
      if (e.key === 'Enter' && q.length >= MIN) { e.preventDefault(); clearTimeout(ui.timer); run(q); }
      return;
    }
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      const n = ui.items.length;
      ui.cursor = (((ui.cursor + (e.key === 'ArrowDown' ? 1 : -1)) % n) + n) % n;
      markCursor();
    } else if (e.key === 'Enter') {
      e.preventDefault();
      openItem(ui.items[Math.max(0, ui.cursor)]);
    }
  }

  /** The box, for the Dashboard's header row: the glyph, the field, the "/" hint, and the panel under it. */
  function field(app) {
    const input = h('input', { type: 'search', class: 'bcv-omni__in', id: 'bcv-omni', placeholder: 'Search everything', autocomplete: 'off', spellcheck: 'false', 'aria-label': 'Search everything: courses, assignments, pages, files, people and Wikipedia' });
    const panel = h('div', { class: 'bcv-omni__panel', id: 'bcv-omni-panel', role: 'listbox', 'aria-label': 'Results' });
    panel.hidden = true;
    const box = h('div', { class: 'bcv-omni__box', id: 'bcv-omni-box' }, [
      h('span', { class: 'bcv-omni__ic', 'aria-hidden': 'true' }, U.svg(IC.search, { size: 15, width: 2 })),
      input,
      h('kbd', { class: 'bcv-omni__key', text: '/', 'aria-hidden': 'true' }),
    ]);
    // the W to the left of the box: Wikipedia's articles among the results, or not (kept in the settings)
    const wikiOn = app?.state?.settings?.search?.wikipedia !== false;
    const wiki = h('button', { type: 'button', class: `bcv-omni__wiki ${wikiOn ? 'is-on' : ''}`, id: 'bcv-omni-wiki', text: 'W', 'aria-pressed': wikiOn ? 'true' : 'false', title: wikiOn ? 'Wikipedia results: on — press to turn off' : 'Wikipedia results: off — press to turn on', onclick: () => {
      if (!ui) return;
      ui.wiki = !ui.wiki;
      wiki.classList.toggle('is-on', ui.wiki);
      wiki.setAttribute('aria-pressed', ui.wiki ? 'true' : 'false');
      wiki.title = ui.wiki ? 'Wikipedia results: on — press to turn off' : 'Wikipedia results: off — press to turn on';
      BCV.settings?.update?.({ search: { wikipedia: ui.wiki } }).catch(() => {});
      if (app?.state?.settings) app.state.settings.search = { ...(app.state.settings.search || {}), wikipedia: ui.wiki };
      if (!ui.panel.hidden && ui.q.length >= MIN) run(ui.q); // (the search on show, asked again with or without it)
      else { ui.items = []; ui.groups = new Map(); ui.q = ''; } // (results kept from before: dropped, so a focus does not bring them back as they were)
    } });
    const root = h('div', { class: 'bcv-omni', id: 'bcv-omni-root' }, [wiki, box, panel]);
    ui = { app, root, input, panel, wiki: wikiOn, seq: 0, q: '', groups: new Map(), pending: 0, cursor: -1, items: [], timer: 0 };
    input.addEventListener('input', () => {
      if (!ui) return;
      clearTimeout(ui.timer);
      const q = norm(input.value);
      if (q.length < MIN) { ui.seq += 1; close(); return; }
      ui.timer = setTimeout(() => run(q), PAUSE);
    });
    input.addEventListener('focus', () => { if (ui && norm(input.value).length >= MIN && ui.items.length && ui.q === norm(input.value)) ui.panel.hidden = false; });
    input.addEventListener('keydown', onKey);
    return root;
  }
  // a press anywhere else closes the panel; "/" (or ⌘K, Ctrl+K) from anywhere on the page puts the cursor in the box
  document.addEventListener('pointerdown', (e) => { if (ui && ui.root.isConnected && !ui.root.contains(e.target)) close(); }, true);
  document.addEventListener('keydown', (e) => {
    const input = document.getElementById('bcv-omni');
    if (!input || !ui) return;
    const t = e.target;
    const typing = t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable);
    const slash = e.key === '/' && !typing && !e.metaKey && !e.ctrlKey && !e.altKey;
    const k = (e.metaKey || e.ctrlKey) && !e.shiftKey && !e.altKey && String(e.key).toLowerCase() === 'k';
    if (!slash && !k) return;
    e.preventDefault();
    input.focus();
    input.select();
  });

  BCV.search = { field, close };
})();
