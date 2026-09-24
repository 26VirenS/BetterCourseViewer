/* Search everything: the box in the Dashboard's header is a hub — find, open and do, from one field,
 * nothing to press. From the first character what the page already holds answers at once — your
 * courses, a command whose name it starts, a sum worked out — and from the second, after a short
 * pause, all of Canvas: for each starred course its assignments, announcements, pages, discussions
 * and files, then people, then Wikipedia, each group under the box as its source answers. The first
 * result is chosen as you type, so Enter is optional and a click is one press; the arrows walk them.
 * A row carries what can be done with it (an assignment: Submit, right here; a file: Download,
 * Convert; a course: Grades, Files). "/" starts a command (content/app/hub.js: /submit, /download,
 * /convert, /open, /todo, /dark …): the list narrows as the name is typed, Tab (or →) completes
 * it, and the argument — an assignment, a file, a tool, a course — is picked from a list that
 * narrows as you type. "/" or ⌘K on any screen brings the Dashboard up with the box focused and
 * whatever was typed meanwhile kept. The Wikipedia lookup goes through the background (background.js
 * 'wiki'), so the page's own rules never block it. The first time, a black screen points at the box
 * (welcome.js: "Search Everything."). */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;
  const C = BCV.canvas;
  const NET_MIN = 2; // characters before Canvas and Wikipedia are asked (what the page holds answers from the first)
  const PAUSE = 280; // ms of quiet after the last keystroke before the network is asked
  const PER = 5; // results a group shows at most
  const CMDS = 3; // commands a plain search lists at most (the name typed starts theirs)

  let ui = null; // the box on the page: { app, root, input, panel, seq, q, groups, pending, cursor, items, timer, mode, cmd, arg, cs }
  let wanted = null; // the box summoned from another screen: { text, stop } — filled and focused once the Dashboard's box mounts
  const norm = (s) => String(s || '').toLowerCase().trim();
  const hit = (s, q) => norm(s).includes(q);
  const nameOf = (c) => c.nickname || c.code || c.name;
  const when = (iso) => { try { return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }); } catch { return ''; } };
  const favs = async () => { try { return (await store.favorites() || []).filter((c) => c.state !== 'past'); } catch { return []; } }; // (the starred courses as the Dashboard has them; every current course when none is starred)
  const LANES = 4; // requests in flight at once across the starred courses (a burst of forty at Canvas trips its rate limit)
  const SUMMON_MS = 8000; // how long keys pressed on the way to a summoned box are kept for it
  /** A queue that runs `n` jobs at a time. */
  const limiter = (n) => { let busy = 0; const queue = []; const next = () => { if (busy >= n || !queue.length) return; busy += 1; queue.shift()().finally(() => { busy -= 1; next(); }); }; return (fn) => new Promise((resolve, reject) => { queue.push(() => Promise.resolve().then(fn).then(resolve, reject)); next(); }); };
  /** The hub — the commands, the answers, a row's actions — loaded the first time the box is focused (content/app/lazy.js). */
  const hubReady = () => (BCV.hub ? Promise.resolve(true) : BCV.lazy?.load ? BCV.lazy.load('hub') : Promise.reject(new Error('no loader')));

  // ---- the sources: each resolves to a group's items (PER at most), or nothing ----------------------
  async function courseHits(q) {
    const cs = await store.courses().catch(() => []);
    return cs.filter((c) => c.state === 'current' && [c.name, c.code, c.nickname].some((s) => hit(s, q))).slice(0, PER)
      .map((c) => ({ icon: IC.book, title: nameOf(c), sub: c.name !== nameOf(c) ? c.name : 'Course', href: `/courses/${c.id}`, tint: c.color || null, course: c }));
  }
  /** One of a course's lists, for every starred course: Canvas narrows it by search_term where it can, and the title is checked here in any case. */
  const perCourse = (q, cs, lane, path, params, pick) => Promise.all(cs.map((c) => lane(async () => {
    try {
      const rows = await C.get(`/api/v1/courses/${c.id}${path}`, { params: { search_term: q, per_page: 20, ...params } });
      return (Array.isArray(rows) ? rows : []).map((r) => pick(r, c)).filter((it) => it && it.title && (hit(it.title, q) || (it.alt && hit(it.alt, q))));
    } catch { return []; } // (a list the course keeps from students — files, often — is no result, not an error)
  }))).then((lists) => lists.flat().slice(0, PER));
  const assignmentHits = (q, cs, lane) => perCourse(q, cs, lane, '/assignments', {}, (a, c) => ({ icon: IC.doc, title: a.name, sub: `${nameOf(c)} · ${a.due_at ? `Due ${when(a.due_at)}` : 'Assignment'}`, href: `/courses/${c.id}/assignments/${a.id}`, assignment: a }));
  const announcementHits = (q, cs, lane) => perCourse(q, cs, lane, '/discussion_topics', { only_announcements: true }, (t, c) => ({ icon: IC.bell, title: t.title, sub: `${nameOf(c)} · Announcement`, href: `/courses/${c.id}/discussion_topics/${t.id}` }));
  const pageHits = (q, cs, lane) => perCourse(q, cs, lane, '/pages', {}, (p, c) => ({ icon: IC.page, title: p.title, sub: `${nameOf(c)} · Page`, href: `/courses/${c.id}/pages/${p.url}` }));
  const discussionHits = (q, cs, lane) => perCourse(q, cs, lane, '/discussion_topics', {}, (t, c) => ({ icon: IC.people, title: t.title, sub: `${nameOf(c)} · Discussion`, href: `/courses/${c.id}/discussion_topics/${t.id}` }));
  const fileHits = (q, cs, lane) => perCourse(q, cs, lane, '/files', {}, (f, c) => ({ icon: IC.folder, title: f.display_name || f.filename, alt: f.filename, sub: `${nameOf(c)} · File`, href: `/courses/${c.id}/files/${f.id}`, file: f }));
  async function peopleHits(q) {
    try {
      const rows = await C.get('/api/v1/search/recipients', { params: { search: q, per_page: 10 } });
      return (Array.isArray(rows) ? rows : []).filter((r) => r && r.name && !/^(course|group|section)_/.test(String(r.id)) && hit(r.name, q)).slice(0, PER)
        .map((r) => { const cid = Object.keys(r.common_courses || {})[0]; return { icon: IC.people, title: r.name, sub: 'Person', href: cid ? `/courses/${cid}/users/${r.id}` : `/users/${r.id}`, person: r }; });
    } catch { return []; }
  }
  async function wikiHits(q) {
    if (!ui?.wiki) return []; // (the W beside the box, off)
    try {
      const r = await Promise.resolve(BCV.api.runtime.sendMessage({ type: 'wiki', q }));
      return r?.ok ? (r.hits || []).slice(0, PER).map((w) => ({ icon: IC.globe, title: w.title, sub: w.text || 'Wikipedia', url: w.url })) : [];
    } catch { return []; }
  }
  // the groups in the order they are shown: what the page holds first, then Canvas, then Wikipedia
  const ORDER = ['Answer', 'Commands', 'Courses', 'Assignments', 'Announcements', 'Pages', 'Discussions', 'Files', 'People', 'Wikipedia'];
  const NET = [
    ['Assignments', (q, cs, lane) => assignmentHits(q, cs, lane)],
    ['Announcements', (q, cs, lane) => announcementHits(q, cs, lane)],
    ['Pages', (q, cs, lane) => pageHits(q, cs, lane)],
    ['Discussions', (q, cs, lane) => discussionHits(q, cs, lane)],
    ['Files', (q, cs, lane) => fileHits(q, cs, lane)],
    ['People', (q) => peopleHits(q)],
    ['Wikipedia', (q) => wikiHits(q)],
  ];

  // ---- a search: what the page holds at once, every network source after a pause, each group painted as it answers ----
  function run(raw) {
    if (!ui) return;
    const seq = ++ui.seq;
    clearTimeout(ui.timer);
    ui.raw = raw; ui.q = norm(raw); ui.groups = new Map(); ui.items = []; ui.pending = 0; ui.cursor = 0; ui.cmd = null; ui.arg = ''; ui.mode = 'plain'; // (the rows of the last search are no answer to this one: Enter meanwhile does nothing)
    if (String(raw).trimStart().startsWith('/')) { commandMode(String(raw).trimStart(), seq); return; }
    const q = ui.q;
    if (!q) { close(); return; }
    const hub = BCV.hub;
    if (hub) {
      const answers = hub.quickAnswers(raw);
      if (answers.length) ui.groups.set('Answer', answers);
      if (q.length >= 2 && !/\s/.test(q)) { // a command whose name the word typed starts: "dark", "todo", "gpa"
        const cmds = hub.matchCommands(q, { strict: true }).slice(0, CMDS).map(commandRow);
        if (cmds.length) ui.groups.set('Commands', cmds);
      }
    }
    courseHits(q).then((items) => { if (ui && ui.seq === seq) { ui.groups.set('Courses', items); paint(); } });
    if (q.length >= NET_MIN) { ui.pending = NET.length; ui.timer = setTimeout(() => network(q, seq), PAUSE); }
    paint();
  }
  async function network(q, seq) {
    const cs = ui.cs || (ui.cs = await favs());
    if (!ui || ui.seq !== seq) return;
    const lane = limiter(LANES);
    for (const [name, fn] of NET) {
      Promise.resolve().then(() => fn(q, cs, lane)).catch(() => []).then((items) => {
        if (!ui || ui.seq !== seq) return;
        ui.groups.set(name, items || []);
        ui.pending -= 1;
        paint();
      });
    }
  }
  /** A command as a row: its name and what it takes, the line under it; Enter runs one that takes nothing, completes one that does. */
  const commandRow = (c) => ({ icon: c.icon, title: `/${c.name}${c.takes ? ` ${c.takes}` : ''}`, sub: c.hint, cmd: c, fill: c.args || c.text ? `/${c.name} ` : null });
  /** "/" and what follows: the commands the name typed could mean, or — the name complete — what the command takes, as a list that narrows with the rest. */
  async function commandMode(raw, seq) {
    ui.mode = 'cmd';
    if (!BCV.hub) {
      ui.pending = 1; paint(); // (the hub lands in a moment: the panel says so meanwhile)
      try { await hubReady(); } catch { if (ui && ui.seq === seq) { ui.pending = 0; close(); } return; }
      if (!ui || ui.seq !== seq) return;
      ui.pending = 0;
    }
    const hub = BCV.hub;
    const p = hub.parse(raw);
    if (!p.cmd) { // the list, narrowed by the name so far
      ui.groups = new Map([['Commands', hub.matchCommands(p.name).map(commandRow)]]);
      paint();
      return;
    }
    const cmd = p.cmd;
    ui.cmd = cmd; ui.arg = p.arg;
    if (cmd.text) { // a command that takes words: the one row is the words typed
      ui.groups = new Map([[cmd.hint, [{ icon: cmd.icon, title: p.arg ? `${cmd.verb || 'Go'}: ${p.arg}` : `Type ${cmd.takes}…`, sub: p.arg ? cmd.hint : `/${cmd.name} ${cmd.takes}`, act: true }]]]);
      paint();
      return;
    }
    if (!cmd.args) { // a command that takes nothing: the one row is the command, Enter runs it
      ui.groups = new Map([[cmd.hint, [{ icon: cmd.icon, title: `/${cmd.name}`, sub: cmd.hint, act: true }]]]);
      paint();
      return;
    }
    ui.pending = 1;
    paint(); // (the last list goes at once; "Searching…" until this one lands)
    const go = async () => {
      const cs = ui.cs || (ui.cs = await favs());
      if (!ui || ui.seq !== seq) return;
      let items = [];
      try { items = await cmd.args(p.arg, { cs, lane: limiter(LANES), q: p.arg }); } catch { items = []; }
      if (!ui || ui.seq !== seq) return;
      ui.pending = 0;
      ui.groups = new Map([[cmd.hint, items || []]]);
      paint();
    };
    if (cmd.net && p.arg) ui.timer = setTimeout(go, PAUSE); else go(); // (a list Canvas is asked for waits for the typing to pause; the page's own answer at once)
  }

  function paint() {
    if (!ui) return;
    const items = [];
    const blocks = [];
    const names = ui.mode === 'cmd' ? [...ui.groups.keys()] : ORDER;
    for (const name of names) {
      const list = ui.groups.get(name);
      if (!list || !list.length) continue;
      const rows = list.map((it) => { items.push(it); return row(it, items.length - 1); });
      blocks.push(h('div', { class: 'bcv-omni__group', dataset: { group: name } }, [h('div', { class: 'bcv-omni__gtitle', text: name }), ...rows]));
    }
    ui.items = items;
    ui.cursor = items.length ? Math.min(Math.max(ui.cursor, 0), items.length - 1) : -1;
    if (!items.length && !ui.pending && ui.mode === 'plain' && ui.q.length < NET_MIN) { ui.panel.hidden = true; return; } // (one letter, nothing on the page that starts with it: nothing to show yet)
    if (!items.length) blocks.push(h('div', { class: 'bcv-omni__empty', text: ui.pending ? 'Searching…' : ui.mode === 'cmd' && ui.cmd ? (ui.arg ? `Nothing for “${ui.arg}”` : ui.cmd.empty || `Type ${ui.cmd.takes || 'more'}…`) : ui.mode === 'cmd' ? 'No command by that name. /help lists them.' : `Nothing for “${ui.input.value.trim()}”` }));
    else if (ui.pending) blocks.push(h('div', { class: 'bcv-omni__more', text: 'Searching…' }));
    ui.panel.replaceChildren(...blocks);
    ui.panel.hidden = false;
    markCursor();
  }
  /** A result's row: its tile and words; what can be done with it as small buttons at the right (shown on the row chosen, or under the pointer). */
  function row(it, i) {
    const acts = it.act ? [] : (BCV.hub?.actionsFor?.(it) || []);
    const el = h('div', { class: `bcv-omni__item${it.answer ? ' bcv-omni__item--ans' : ''}`, role: 'option', tabindex: '-1', 'aria-selected': 'false', dataset: { i }, onclick: (e) => { if (e.target.closest?.('.bcv-omni__act')) return; openItem(it, el); } }, [
      h('span', { class: 'bcv-omni__iic', style: it.tint ? { color: it.tint } : null }, U.svg(it.icon, { size: 15, width: 1.9 })),
      h('span', { class: 'bcv-omni__body' }, [h('span', { class: 'bcv-omni__t', text: it.title }), it.sub ? h('span', { class: 'bcv-omni__s', text: it.sub }) : null]),
      acts.length ? h('span', { class: 'bcv-omni__acts' }, acts.map((a) => h('button', { type: 'button', class: 'bcv-omni__act', title: a.label, onclick: (e) => { e.stopPropagation(); act(a, e.currentTarget); } }, [U.svg(a.icon, { size: 12, width: 2 }), a.label]))) : null,
      it.fill ? h('kbd', { class: 'bcv-omni__hint', text: 'Tab', 'aria-hidden': 'true' }) : null,
      it.url ? h('span', { class: 'bcv-omni__ext', title: 'Opens in a new tab' }, U.svg(IC.external, { size: 12, width: 2 })) : null,
    ]);
    return el;
  }
  function markCursor({ scroll = false } = {}) {
    if (!ui) return;
    ui.panel.querySelectorAll('.bcv-omni__item').forEach((el, i) => { const on = i === ui.cursor; el.classList.toggle('is-cur', on); el.setAttribute('aria-selected', on ? 'true' : 'false'); });
    const cur = ui.panel.querySelector('.bcv-omni__item.is-cur');
    if (cur && scroll) cur.scrollIntoView({ block: 'nearest' });
  }
  function close() {
    if (!ui) return;
    clearTimeout(ui.timer);
    ui.panel.hidden = true;
    ui.cursor = -1;
    ui.seq += 1; // (a source still answering is answering a search that is closed: it paints nothing)
    ui.pending = 0;
  }
  /** The words in the box replaced (a command completed, a /help row chosen) and searched again, the cursor at the end. */
  function fill(text) {
    if (!ui) return;
    ui.input.value = text;
    ui.input.focus();
    try { ui.input.setSelectionRange(text.length, text.length); } catch { /* a search input in some browsers refuses: the caret is at the end anyway */ }
    run(text);
  }
  /** What a row does: a command's argument runs the command; a command row runs or completes it; a Canvas item opens in place; a Wikipedia article in a new tab; an answer copies itself. */
  function openItem(it, from = null) {
    if (!it || !ui) return;
    const { app } = ui;
    if (ui.mode === 'cmd' && ui.cmd) { runCommand(ui.cmd, it.act ? null : it, from); return; }
    if (it.cmd) { if (it.fill) fill(it.fill); else runCommand(it.cmd, null, from); return; }
    close();
    if (it.url) window.open(it.url, '_blank', 'noopener');
    else if (it.run) it.run(from);
    else if (it.href) app.go(it.href);
  }
  function runCommand(cmd, item, from) {
    if (!ui || !BCV.hub) return;
    const ctx = { q: ui.arg, cs: ui.cs || [], lane: limiter(LANES), close, fill, from, app: ui.app };
    try { const r = cmd.run(item, ctx); if (r && typeof r.catch === 'function') r.catch((e) => U.toast(`${cmd.name} failed: ${e?.message || e}`, { error: true })); } catch (e) { U.toast(`${cmd.name} failed: ${e?.message || e}`, { error: true }); }
  }
  function act(a, from) {
    close();
    try { const r = a.run(from); if (r && typeof r.catch === 'function') r.catch((e) => U.toast(`${a.label} failed: ${e?.message || e}`, { error: true })); } catch (e) { U.toast(`${a.label} failed: ${e?.message || e}`, { error: true }); }
  }
  const caretAtEnd = () => { try { return ui.input.selectionStart === null || ui.input.selectionStart === ui.input.value.length; } catch { return true; } };
  function onKey(e) {
    if (!ui) return;
    if (e.key === 'Escape') {
      if (!ui.panel.hidden) { close(); e.preventDefault(); e.stopPropagation(); } // (the panel goes, the words stay: a search field would clear itself)
      else ui.input.blur();
      return;
    }
    const open = !ui.panel.hidden && ui.items.length > 0;
    if (e.key === 'Enter') {
      e.preventDefault();
      if (open) openItem(ui.items[Math.max(0, ui.cursor)], ui.panel.querySelector('.bcv-omni__item.is-cur'));
      else if (norm(ui.input.value)) run(ui.input.value); // (a closed panel: the same words, asked again)
      return;
    }
    if (!open) return;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
      const n = ui.items.length;
      ui.cursor = (((ui.cursor + (e.key === 'ArrowDown' ? 1 : -1)) % n) + n) % n;
      markCursor({ scroll: true });
    } else if (e.key === 'Tab' || (e.key === 'ArrowRight' && caretAtEnd())) {
      const it = ui.items[Math.max(0, ui.cursor)];
      if (it?.fill) { e.preventDefault(); fill(it.fill); } // a command's name completed
      else if (e.key === 'ArrowRight') { const b = ui.panel.querySelector('.bcv-omni__item.is-cur .bcv-omni__act'); if (b) { e.preventDefault(); b.focus(); } } // → onto the row's first action
    }
  }
  /** The keys on a row's action buttons: ← → between them, ↑ ↓ back to the rows, Escape back to the box. */
  function onActKey(e) {
    const b = e.target.closest?.('.bcv-omni__act');
    if (!b || !ui) return;
    if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') {
      const next = e.key === 'ArrowRight' ? b.nextElementSibling : b.previousElementSibling;
      e.preventDefault();
      if (next) next.focus(); else if (e.key === 'ArrowLeft') ui.input.focus();
    } else if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Escape') {
      ui.input.focus();
      if (e.key !== 'Escape') onKey(e);
      else e.preventDefault();
    }
  }

  /** The box, for the Dashboard's header row: the glyph, the field, the "/" hint, and the panel under it. */
  function field(app) {
    const input = h('input', { type: 'search', class: 'bcv-omni__in', id: 'bcv-omni', placeholder: 'Search everything', autocomplete: 'off', spellcheck: 'false', 'aria-label': 'Search everything: courses, assignments, pages, files, people and Wikipedia — or type / for a command', 'aria-controls': 'bcv-omni-panel', 'aria-autocomplete': 'list' });
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
      if (!ui.panel.hidden && ui.mode === 'plain' && ui.q.length >= NET_MIN) run(ui.input.value); // (the search on show, asked again with or without it)
      else { ui.items = []; ui.groups = new Map(); ui.q = ''; } // (results kept from before: dropped, so a focus does not bring them back as they were)
    } });
    const root = h('div', { class: 'bcv-omni', id: 'bcv-omni-root' }, [wiki, box, panel]);
    ui = { app, root, input, panel, wiki: wikiOn, seq: 0, q: '', raw: '', groups: new Map(), pending: 0, cursor: -1, items: [], timer: 0, mode: 'plain', cmd: null, arg: '', cs: null };
    input.addEventListener('input', () => { if (ui) run(input.value); });
    input.addEventListener('focus', () => {
      if (!ui) return;
      hubReady().then(() => { if (ui && !ui.panel.hidden && ui.mode === 'plain') paint(); }).catch(() => {}); // (the rows' actions, once the hub is here)
      if (ui.items.length && ui.q === norm(input.value) && ui.panel.hidden) { ui.cursor = Math.max(0, ui.cursor); ui.panel.hidden = false; markCursor(); }
    });
    input.addEventListener('keydown', onKey);
    panel.addEventListener('keydown', onActKey);
    // (summoned from another screen: summon() lands the words typed on the way once the Dashboard's draw resolves)
    return root;
  }
  // a press anywhere else closes the panel; "/" (or ⌘K, Ctrl+K) from anywhere on the page puts the cursor in the box — on another screen, the Dashboard comes up with the box focused and whatever is typed meanwhile kept
  document.addEventListener('pointerdown', (e) => { if (ui && ui.root.isConnected && !ui.root.contains(e.target)) close(); }, true);
  const summonable = () => {
    const app = BCV.app;
    const r = app?.state?.route;
    if (!app || !r || !document.getElementById('bcv-app') || BCV.phone?.active?.()) return false;
    if (r.screen === 'native' || r.params?.get?.('bcv') === 'native' || app.state.quizOpen || document.documentElement.classList.contains('bcv-quiz')) return false;
    return !document.querySelector('.bcv-sheet-ov, .bcv-viewer-ov, #bcv-welcome, #bcv-setup, .bcv-menu');
  };
  function summon() {
    const w = { text: '', at: Date.now(), done: false };
    const keep = (e) => {
      if (w.done || Date.now() - w.at > 8000) { w.stop(); return; }
      if (e.key === 'Backspace') { w.text = w.text.slice(0, -1); e.preventDefault(); } else if (e.key.length === 1 && !e.metaKey && !e.ctrlKey && !e.altKey) { w.text += e.key; e.preventDefault(); }
    };
    w.stop = () => { w.done = true; if (wanted === w) wanted = null; document.removeEventListener('keydown', keep, true); };
    document.addEventListener('keydown', keep, true);
    wanted = w;
    setTimeout(() => { if (!w.done) w.stop(); }, SUMMON_MS); // (a Dashboard that never came: the keys go back to the page)
    // in place, the Dashboard's draw is a promise that resolves with its box on the page; a page
    // load instead (from a Canvas-drawn page) takes the keys with it, and the wait above ends it
    Promise.resolve(BCV.app.go('/')).then(() => {
      if (w.done || !ui || !ui.input.isConnected) return;
      ui.input.value = w.text;
      w.stop(); // (from here the keys go to the box itself)
      ui.input.focus();
      if (w.text) run(w.text);
    }).catch(() => w.stop());
  }
  document.addEventListener('keydown', (e) => {
    const t = e.target;
    const typing = t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable);
    const slash = e.key === '/' && !typing && !e.metaKey && !e.ctrlKey && !e.altKey;
    const k = (e.metaKey || e.ctrlKey) && !e.shiftKey && !e.altKey && String(e.key).toLowerCase() === 'k';
    if (!slash && !k) return;
    const input = document.getElementById('bcv-omni');
    if (input && ui) { e.preventDefault(); input.focus(); input.select(); return; }
    if (!summonable() || (k && typing)) return;
    e.preventDefault();
    summon();
  });

  BCV.search = { field, close, summon };
})();
