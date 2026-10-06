/* Search everything: the box in the Dashboard's header — and on every other screen at the left of the widgets' bar across the top (2.98.70) — is a hub — find, open and do, from one field,
 * nothing to press. From the first character what the page already holds answers at once — your
 * courses, a command whose name it starts, a sum worked out — and from the second, after a short
 * pause, all of Canvas: for each starred course its assignments, announcements, pages, discussions
 * and files, then people, then Wikipedia, each group under the box as its source answers. The first
 * result is chosen as you type, so Enter is optional and a click is one press; the arrows walk them.
 * A row carries what can be done with it (an assignment: Submit, right here; a file: Download,
 * Convert; a course: Grades, Files). "/" starts a command (content/app/hub.js: /submit, /download,
 * /convert, /open, /todo, /dark …): the list narrows as the name is typed, Tab (or →) completes
 * it, and the argument — an assignment, a file, a tool, a course — is picked from a list that
 * narrows as you type. "/" or ⌘K on any screen puts the cursor in it (where the bar is not shown — Canvas's own page
 * in the shell — the Dashboard comes up with the box focused and whatever was typed meanwhile kept). On a phone the
 * same box sits under Today's title: the results take
 * the screen while there are any, every row's actions stay in view (nothing to hover), and the keyboard
 * goes once a row is chosen. The Wikipedia lookup goes through the background (background.js
 * 'wiki'), so the page's own rules never block it. The first time, a black screen points at the box
 * (welcome.js: "Search Everything."). On the desktop the box floats (2.98.54): the moment it has the
 * cursor it lifts out of the header to the middle of the window as a large pill over the page, a little
 * dimmed (the page stays sharp since 2.98.69) — Spotlight's way — and under it, before anything is typed, the four kinds to search in:
 * Courses ⌘1, Work ⌘2 (assignments, quizzes, discussions), Files ⌘3 and Actions ⌘4 (the commands). A
 * kind chosen sits in the box as a chip and lists its own things at once (your courses; what is due
 * this week; your files; every command), and what is typed then searches that kind alone; Backspace
 * on an empty box lets the kind go, Escape steps back (the results, then the kind, then the box goes
 * home), and a press anywhere else puts the box back in the header. The phone's box stays where it
 * is, and so does the box during the tour. Files afloat (⌘3, 2.98.69) are Spotlight's grid: the kinds found as wide chips across
 * the top (one pressed narrows the grid to it), then each file as a page of its kind (a picture as its own thumbnail) with its
 * name under it — the files and pages changed last before anything is typed (Recents), what is typed narrowing them; the arrows
 * walk the grid (← → along a row once ↓ has gone into it, ↑ ↓ to the tile nearest above or below). */
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
  const GRID_N = 21, RECENTS = 28; // the Files grid afloat: results a group shows at most (three rows), the recent ones before anything is typed (four)

  let ui = null; // the box on the page: { app, root, input, panel, seq, q, groups, pending, cursor, items, timer, mode, cmd, arg, cs }
  let widthWatch = null; // (the panel's width, for the highlight: one at a time)
  let docked = null; // the box built for the top bar (dock()), while it is the one in use
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

  // ---- the box afloat (2.98.54) ------------------------------------------------------------------
  // On the desktop the box lifts out of the header the moment it has the cursor: its root is moved into
  // a fixed palette (.bcv-spot) that eases from the header's place to the middle of the window (left,
  // top and width transition; the box grows from 29px to 72px on the way) over a light dim
  // (.bcv-spot-ov, which takes no pointer: a press anywhere on the page still lands, and puts the box
  // back), while a ghost of the same height keeps the header's row as it was. Under it, the four kinds
  // to search in, then what is typed. Escape and a press elsewhere fold it back the same way.
  const SCOPES = [
    { key: 'courses', label: 'Courses', hint: 'Every course you are in', icon: IC.book, groups: ['Courses'], title: 'Your courses' },
    { key: 'work', label: 'Work', hint: 'Assignments, quizzes and discussions', icon: IC.doc, groups: ['Best match', 'Assignments', 'Discussions'], title: 'Due this week' },
    { key: 'files', label: 'Files', hint: 'Course files and pages', icon: IC.folder, groups: ['Files', 'Pages'], title: 'Recents' },
    { key: 'actions', label: 'Actions', hint: 'Commands: /submit, /open, /todo, /dark…', icon: IC.bolt, groups: ['Commands'], title: 'Commands' },
  ];
  const SPOT_W = 680, SPOT_MS = 470; // the pill's width at most; the float's length (app.css .46s)
  let spot = null; // afloat: { ov, pal, ghost, timer, onResize, folding }
  const afloat = () => !!spot && !spot.folding;
  const canFloat = () => !!ui && ui.root.isConnected && !BCV.phone?.active?.() && !document.getElementById('bcv-tour') && !document.getElementById('bcv-setup');
  const still = () => !!U.reducedMotion?.();
  const gridMode = () => afloat() && ui?.scope?.key === 'files'; // (Files afloat: Spotlight's grid of tiles)
  const cap = () => (gridMode() ? GRID_N : PER); // (a group's results at most: the grid shows rows of them)
  function place() {
    if (!spot) return;
    const vw = innerWidth, vh = innerHeight, w = Math.min(SPOT_W, vw - 32);
    Object.assign(spot.pal.style, { left: `${Math.round((vw - w) / 2)}px`, top: `${Math.round(Math.min(vh * 0.18, 160))}px`, width: `${w}px` });
  }
  /** A fold under way finished at once: the box home (or dropped, if the header has been drawn afresh), the palette gone. */
  function settle() {
    const s = spot;
    if (!s) return;
    clearTimeout(s.timer); clearTimeout(s.lift); clearTimeout(s.closer);
    window.removeEventListener('resize', s.onResize);
    if (s.folding) close(); // (a fold cut short: what its timer would have done)
    if (s.ghost.isConnected && ui) s.ghost.replaceWith(ui.root); else { s.ghost.remove(); ui?.root.remove(); }
    s.pal.remove(); s.ov.remove();
    spot = null;
  }
  function float() {
    if (!canFloat()) return;
    if (spot) { if (!spot.folding) return; settle(); } // (a press on the box as it folds back: home at once, then up again)
    const r = ui.root.getBoundingClientRect();
    const ghost = h('div', { class: 'bcv-omni bcv-omni--ghost', 'aria-hidden': 'true', style: { height: `${Math.round(r.height)}px` } });
    ui.root.replaceWith(ghost);
    const pal = h('div', { class: `bcv-spot is-far${still() ? ' is-still' : ''}`, id: 'bcv-spot', style: { left: `${Math.round(r.left)}px`, top: `${Math.round(r.top)}px`, width: `${Math.round(r.width)}px` } }, [ui.root]);
    const ov = h('div', { class: 'bcv-spot-ov', 'aria-hidden': 'true' });
    document.body.append(ov, pal);
    spot = { ov, pal, ghost, timer: 0, lift: 0, closer: 0, folding: false, onResize: () => place() };
    run(ui.input.value); // (the kinds, or the words' results — painted while the pill still stands at the header, so the panel can fade in as the pill lands rather than ride up with it)
    void pal.offsetWidth; // (the header's place is where it starts from: fixed before the move)
    pal.classList.remove('is-far');
    pal.classList.add('is-lifting'); // (the panel arrives with the pill, not before it)
    spot.lift = setTimeout(() => pal.classList.remove('is-lifting'), 600);
    place();
    window.addEventListener('resize', spot.onResize);
    ui.input.focus(); // (the move took the cursor)
  }
  function unfloat() {
    if (!spot || spot.folding) return;
    const s = spot;
    s.folding = true;
    clearTimeout(s.lift);
    s.pal.classList.remove('is-lifting');
    if (document.activeElement === ui.input) ui.input.blur();
    const r = s.ghost.isConnected ? s.ghost.getBoundingClientRect() : null;
    s.ov.classList.add('is-folding');
    s.pal.classList.add('is-far'); // (the panel fades as the pill sets off; hidden for good once it has)
    if (r) Object.assign(s.pal.style, { left: `${Math.round(r.left)}px`, top: `${Math.round(r.top)}px`, width: `${Math.round(r.width)}px` });
    clearScope({ quiet: true }); // (the kind's chip goes at once: one vanishing mid-fold would show)
    s.closer = setTimeout(close, still() ? 0 : 160);
    s.timer = setTimeout(settle, still() ? 0 : SPOT_MS);
  }
  const scopeRows = () => SCOPES.map((s, i) => ({ icon: s.icon, title: s.label, sub: s.hint, scope: s, key: `⌘${i + 1}` }));
  /** The four kinds, listed under an empty box afloat. */
  function paintScopes() {
    if (!ui) return;
    ui.seq += 1; clearTimeout(ui.timer); ui.pending = 0; ui.mode = 'scopes'; ui.cmd = null; ui.arg = ''; ui.q = ''; ui.raw = ''; ui.cursor = 0;
    ui.groups = new Map([['Search in', scopeRows()]]);
    paint();
  }
  /** A kind's own things, before anything is typed: your courses (the starred first), what is due this week, your files, every command. */
  async function listScope() {
    if (!ui || !ui.scope) return;
    const s = ui.scope, seq = ++ui.seq;
    clearTimeout(ui.timer); ui.groups = new Map(); ui.items = []; ui.cursor = 0; ui.mode = 'scope'; ui.cmd = null; ui.arg = ''; ui.q = ''; ui.raw = ''; ui.pending = 1; ui.walk = false;
    paint();
    let items = [];
    try {
      if (s.key === 'courses') {
        const [cs, fs] = await Promise.all([store.courses().catch(() => []), favs()]);
        const star = new Set(fs.map((c) => String(c.id)));
        items = cs.filter((c) => c.state === 'current').sort((a, b) => (star.has(String(b.id)) ? 1 : 0) - (star.has(String(a.id)) ? 1 : 0)).slice(0, 12).map(courseRow);
      } else if (s.key === 'files') { // (the grid: the files and pages changed last, newest first — from the index, waited for while it is still coming)
        warm().catch(() => {});
        await Promise.race([Promise.all([kindReady('Files'), kindReady('Pages')]), new Promise((r) => setTimeout(r, 8000))]);
        const kept = (k) => (ix?.kinds[k]?.ready ? ix.kinds[k].items : []);
        items = [...kept('Files'), ...kept('Pages')].sort((a, b) => (b.at ?? 0) - (a.at ?? 0)).slice(0, RECENTS);
      } else {
        await hubReady();
        if (s.key === 'actions') items = (BCV.hub.matchCommands('') || BCV.hub.COMMANDS || []).map(commandRow);
        else { const cs = ui.cs || (ui.cs = await favs()); items = await BCV.hub.byName(s.key === 'work' ? 'due' : 'file').args(s.key === 'work' ? 'week' : '', { cs, lane: limiter(LANES), q: '' }); }
      }
    } catch { items = []; }
    if (!ui || ui.seq !== seq) return;
    ui.pending = 0;
    ui.groups = new Map([[s.title, items || []]]);
    paint();
  }
  function setScope(s) {
    if (!ui || !afloat()) return;
    ui.scope = s; ui.fkind = null;
    ui.chip.replaceChildren(U.svg(s.icon, { size: 13, width: 2 }), h('span', { text: s.label }), h('button', { type: 'button', class: 'bcv-omni__chipx', title: 'Search everything again', 'aria-label': `Stop searching ${s.label.toLowerCase()} only`, onclick: (e) => { e.stopPropagation(); clearScope(); ui.input.focus(); } }, U.svg(IC.close, { size: 10, width: 2.4 })));
    ui.chip.hidden = false;
    ui.input.placeholder = `Search ${s.label.toLowerCase()}`;
    ui.input.setAttribute('aria-label', `Search ${s.label.toLowerCase()}`);
    ui.input.focus();
    run(ui.input.value);
  }
  function clearScope({ quiet = false } = {}) {
    if (!ui || !ui.scope) return;
    ui.scope = null; ui.fkind = null;
    ui.chip.hidden = true; ui.chip.replaceChildren();
    ui.input.placeholder = ui.placeholder;
    ui.input.setAttribute('aria-label', ui.ariaLabel);
    if (!quiet) run(ui.input.value);
  }

  // ---- the sources: each resolves to a group's items (PER at most), or nothing ----------------------
  const courseRow = (c) => ({ icon: IC.book, title: nameOf(c), sub: c.name !== nameOf(c) ? c.name : 'Course', href: `/courses/${c.id}`, tint: c.color || null, course: c });
  async function courseHits(q) {
    const cs = await store.courses().catch(() => []);
    return cs.filter((c) => c.state === 'current' && [c.name, c.code, c.nickname].some((s) => hit(s, q))).slice(0, PER).map(courseRow);
  }
  // a course's item as a row, one way for each kind, whether it came from the index or from Canvas's own search
  const stamp = (iso) => { const t = iso ? Date.parse(iso) : NaN; return Number.isFinite(t) ? t : null; };
  const PICK = {
    Assignments: (a, c) => ({ icon: IC.doc, title: a.name, sub: `${nameOf(c)} · ${a.due_at ? `Due ${when(a.due_at)}` : 'Assignment'}`, href: `/courses/${c.id}/assignments/${a.id}`, assignment: a, at: stamp(a.due_at) }),
    Announcements: (t, c) => ({ icon: IC.bell, title: t.title, sub: `${nameOf(c)} · Announcement`, href: `/courses/${c.id}/discussion_topics/${t.id}`, at: stamp(t.posted_at || t.created_at) }),
    Pages: (p, c) => ({ icon: IC.page, title: p.title, sub: `${nameOf(c)} · Page`, href: `/courses/${c.id}/pages/${p.url}`, at: stamp(p.updated_at) }),
    Discussions: (t, c) => (t.is_announcement ? null : { icon: IC.people, title: t.title, sub: `${nameOf(c)} · Discussion`, href: `/courses/${c.id}/discussion_topics/${t.id}`, at: stamp(t.last_reply_at || t.posted_at) }),
    Files: (f, c) => ({ icon: IC.folder, title: f.display_name || f.filename, alt: f.filename, sub: `${nameOf(c)} · File`, href: `/courses/${c.id}/files/${f.id}`, file: f, at: stamp(f.updated_at) }),
  };
  /** One of a course's lists, for every course given: Canvas narrows it by search_term where it can, and the title is checked here in any case. */
  const PATHS = { Assignments: ['/assignments', {}], Announcements: ['/discussion_topics', { only_announcements: true }], Pages: ['/pages', {}], Discussions: ['/discussion_topics', {}], Files: ['/files', {}] };
  const perCourse = (kind) => (q, cs, lane) => Promise.all(cs.map((c) => lane(async () => {
    try {
      const [path, params] = PATHS[kind];
      const rows = await C.get(`/api/v1/courses/${c.id}${path}`, { params: { search_term: q, per_page: 20, ...params } });
      return (Array.isArray(rows) ? rows : []).map((r) => PICK[kind](r, c)).filter((it) => it && it.title && (hit(it.title, q) || (it.alt && hit(it.alt, q))));
    } catch { return []; } // (a list the course keeps from students — files, often — is no result, not an error)
  }))).then((lists) => lists.flat().slice(0, PER));
  async function peopleHits(q) {
    try {
      const rows = await C.get('/api/v1/search/recipients', { params: { search: q, per_page: 10 } });
      return (Array.isArray(rows) ? rows : []).filter((r) => r && r.name && !/^(course|group|section)_/.test(String(r.id)) && hit(r.name, q)).slice(0, PER)
        .map((r) => { const cid = Object.keys(r.common_courses || {})[0]; return { icon: IC.people, title: r.name, sub: 'Person', href: cid ? `/courses/${cid}/users/${r.id}` : `/users/${r.id}`, person: r }; });
    } catch { return []; }
  }
  async function wikiHits(q) {
    if (!ui?.wiki) return []; // (the Wikipedia switch in the box, off)
    try {
      const r = await Promise.resolve(BCV.api.runtime.sendMessage({ type: 'wiki', q }));
      return r?.ok ? (r.hits || []).slice(0, PER).map((w) => ({ icon: IC.globe, title: w.title, sub: w.text || 'Wikipedia', url: w.url })) : [];
    } catch { return []; }
  }
  // the groups in the order they are shown: what the page holds first, then a phrase read as one, then Canvas, then Wikipedia
  const ORDER = ['Answer', 'Best match', 'Commands', 'Courses', 'Assignments', 'Announcements', 'Pages', 'Discussions', 'Files', 'People', 'Wikipedia'];
  const IX_KINDS = ['Assignments', 'Announcements', 'Pages', 'Discussions', 'Files'];
  const NET = [
    ...IX_KINDS.map((k) => [k, perCourse(k)]),
    ['People', (q) => peopleHits(q)],
    ['Wikipedia', (q) => wikiHits(q)],
  ];

  // ---- the index (2.98.65) --------------------------------------------------------------------------
  // What the starred courses hold — their assignments, announcements, pages, discussions and files —
  // kept here from the moment the box is first focused (the store's own lists, which the screens share:
  // one request per list and course, a few at a time, the assignments first), so every letter typed is
  // answered from memory at once instead of asking Canvas again, course by course and kind by kind, after
  // a pause. A kind not in yet is asked for the old way meanwhile, and painted from the index the moment it
  // lands; a course whose list could not be read (or whose files run past what is kept) is asked for that
  // way too, alongside. People and Wikipedia are Canvas's and Wikipedia's to answer, after a short pause.
  const IX_TTL = 5 * 60 * 1000; // (taken again on the next focus after this)
  const PAUSE_IX = 140; // ms of quiet before the sources that are not kept here are asked, once the index answers the rest
  const LISTS = {
    Assignments: (c) => store.assignments(c.id),
    Announcements: (c) => store.announcements(c.id),
    Pages: (c) => store.pages(c.id),
    Discussions: (c) => store.discussions(c.id),
    Files: (c) => store.courseFiles(c.id),
  };
  let ix = null; // { at, ids, kinds: { [kind]: { items, ready, partial: Set(course ids) } } }
  const fresh = () => !!ix && Date.now() - ix.at < IX_TTL;
  const waiting = []; // [kind, resolve]: a kind's lists awaited (the Files grid's recents)
  const kindReady = (kind) => (ix?.kinds[kind]?.ready ? Promise.resolve() : new Promise((r) => waiting.push([kind, r])));
  /** Take (or keep) the index for the starred courses; each kind is searchable the moment its lists are in. */
  async function warm() {
    if (fresh()) return;
    const prev = ix;
    // (a kind already kept goes on answering from what it had until its fresh lists are in)
    const built = { at: Date.now(), ids: '', kinds: Object.fromEntries(IX_KINDS.map((k) => [k, prev?.kinds[k]?.ready ? { ...prev.kinds[k] } : { items: [], ready: false, partial: new Set() }])) };
    ix = built;
    const cs = (ui && ui.cs) || await favs();
    if (ui && !ui.cs) ui.cs = cs;
    if (ix !== built) return;
    built.ids = cs.map((c) => String(c.id)).join(',');
    const lane = limiter(LANES);
    await Promise.all(IX_KINDS.map(async (kind) => {
      const partial = new Set();
      const lists = await Promise.all(cs.map((c) => lane(() => LISTS[kind](c).then((rows) => {
        if (kind === 'Files' && Array.isArray(rows) && rows.length >= (store.COURSE_FILES_MAX || 300)) partial.add(String(c.id)); // (more than is kept: Canvas is asked for this course's too)
        return Array.isArray(rows) ? rows : [];
      }).catch((e) => {
        if (![401, 403, 404].includes(e?.status)) partial.add(String(c.id)); // (a list the course keeps from students is no result; one that failed is asked for again by name)
        return [];
      }))));
      if (ix !== built) return;
      const items = [];
      lists.forEach((rows, i) => { for (const r of rows) { const it = PICK[kind](r, cs[i]); if (it && it.title) { it.n = norm(it.title); it.na = it.alt ? norm(it.alt) : ''; items.push(it); } } });
      built.kinds[kind] = { items, ready: true, partial };
      indexed(kind);
    }));
  }
  /** The index's best for the words typed: every word in the title (or a file's name), a word's start and the title's start counting for more, then what is nearest now. */
  function lookup(kind, q, n = PER) {
    const K = ix?.kinds[kind];
    if (!K?.ready) return null;
    const words = q.split(/\s+/).filter(Boolean);
    const now = Date.now();
    const found = [];
    for (const it of K.items) {
      let score = 0;
      for (const w of words) {
        let s = it.n.indexOf(w), src = it.n;
        if (s < 0 && it.na) { s = it.na.indexOf(w); src = it.na; }
        if (s < 0) { score = -1; break; }
        score += s === 0 ? 3 : /[^a-z0-9]/.test(src[s - 1]) ? 2 : 1;
      }
      if (score > 0) found.push([score, it.at === null ? Infinity : Math.abs(it.at - now), it]);
    }
    found.sort((a, b) => b[0] - a[0] || a[1] - b[1]);
    return found.slice(0, n).map((f) => f[2]);
  }
  /** A kind just in: the search on show takes it from the index (what Canvas was asked meanwhile is no longer needed for it). */
  function indexed(kind) {
    for (let i = waiting.length - 1; i >= 0; i--) if (waiting[i][0] === kind) waiting.splice(i, 1)[0][1]();
    if (!ui || ui.mode !== 'plain' || ui.q.length < NET_MIN || !ui.local || ui.local.has(kind)) return;
    const s = ui.scope;
    if (s && !s.groups.includes(kind)) return;
    const items = lookup(kind, ui.q, cap());
    if (!items) return;
    ui.local.add(kind);
    if (!ix.kinds[kind].partial.size) ui.groups.set(kind, items);
    else ui.groups.set(kind, merge(items, ui.groups.get(kind) || []));
    paint();
  }
  /** The index's rows and Canvas's for the same kind, one list: no row twice, the index's first. */
  const merge = (a, b) => { const seen = new Set(); const out = []; for (const it of [...a, ...b]) { const k = it.href || it.url || it.title; if (!seen.has(k)) { seen.add(k); out.push(it); } } return out.slice(0, cap()); };

  /** The same search without the box (2.98.104: the iPhone app's own search field, content/app/native-app.js):
   *  the groups for the words, in the order the box shows them — the index first, Canvas for what it does not hold. */
  async function find(raw) {
    const q = norm(raw);
    if (!q) return [];
    const groups = new Map([['Courses', await courseHits(q)]]);
    if (q.length >= NET_MIN) {
      const cs = await favs();
      await Promise.race([warm().catch(() => {}), new Promise((r) => setTimeout(r, 4000))]);
      const lane = limiter(LANES);
      await Promise.all([
        ...IX_KINDS.map(async (k) => {
          const local = lookup(k, q, PER);
          groups.set(k, local && !ix.kinds[k].partial.size ? local : merge(local || [], await perCourse(k)(q, cs, lane).catch(() => [])));
        }),
        peopleHits(q).then((rows) => groups.set('People', rows)),
      ]);
    }
    return ORDER.filter((k) => groups.get(k)?.length).map((k) => [k, groups.get(k)]);
  }

  // ---- a search: what the page holds at once, the index at once, the rest after a pause, each group painted as it answers ----
  function run(raw) {
    if (!ui) return;
    const seq = ++ui.seq;
    clearTimeout(ui.timer);
    ui.raw = raw; ui.q = norm(raw); ui.groups = new Map(); ui.items = []; ui.pending = 0; ui.cursor = 0; ui.cmd = null; ui.arg = ''; ui.mode = 'plain'; ui.reading = ''; ui.local = new Set(); ui.walk = false; // (the rows of the last search are no answer to this one: Enter meanwhile does nothing)
    if (String(raw).trimStart().startsWith('/')) { commandMode(String(raw).trimStart(), seq); return; }
    const q = ui.q;
    if (!q) { if (afloat()) { if (ui.scope) listScope(); else paintScopes(); } else close(); return; } // (afloat and empty: the kinds, or the kind's own things)
    if (!fresh()) warm().catch(() => {}); // (summoned straight to words, or kept past its time: the index is taken now)
    const s = ui.scope; // (a kind chosen: its groups alone answer)
    const want = (name) => !s || s.groups.includes(name);
    const hub = BCV.hub;
    if (s?.key === 'actions') { // the commands alone, by name
      const list = () => { if (!ui || ui.seq !== seq) return; ui.groups.set('Commands', BCV.hub.matchCommands(q).map(commandRow)); paint(); };
      if (hub) list(); else { ui.pending = 1; paint(); hubReady().then(() => { if (ui && ui.seq === seq) { ui.pending = 0; list(); } }).catch(() => {}); }
      return;
    }
    if (hub && !s) {
      const answers = hub.quickAnswers(raw);
      if (answers.length) ui.groups.set('Answer', answers);
      if (q.length >= 2 && !/\s/.test(q)) { // a command whose name the word typed starts: "dark", "todo", "gpa"
        const cmds = hub.matchCommands(q, { strict: true }).slice(0, CMDS).map(commandRow);
        if (cmds.length) ui.groups.set('Commands', cmds);
      }
    }
    if (want('Courses')) courseHits(q).then((items) => { if (ui && ui.seq === seq) { ui.groups.set('Courses', items); paint(); } });
    if (q.length >= NET_MIN) {
      for (const k of IX_KINDS) {
        if (!want(k)) continue;
        const items = lookup(k, q, cap());
        if (items) { ui.groups.set(k, items); ui.local.add(k); }
      }
    }
    // what the index does not hold (yet): Canvas's own search, after a pause — a short one when the index answered the rest
    const net = NET.filter(([name]) => want(name) && (!ui.local.has(name) || ix.kinds[name].partial.size));
    const slow = net.some(([name]) => IX_KINDS.includes(name) && !ui.local.has(name));
    if (q.length >= NET_MIN && net.length) { ui.pending = net.length; ui.timer = setTimeout(() => network(q, seq, net, want('Best match')), slow ? PAUSE : PAUSE_IX); }
    paint();
  }
  async function network(q, seq, net = NET, phrases = true) {
    const cs = ui.cs || (ui.cs = await favs());
    if (!ui || ui.seq !== seq) return;
    const lane = limiter(LANES);
    // the words read as a phrase — "physics lab due this week", "what's due tomorrow" — answered under Best match, the phrase read back as its title (hub.js understand/resolve)
    if (!BCV.hub) { try { await hubReady(); } catch { /* the hub is not here: the sources below answer */ } if (!ui || ui.seq !== seq) return; }
    const phrase = phrases && BCV.hub ? BCV.hub.understand(ui.raw, cs) : null;
    if (phrase?.strong) {
      ui.pending += 1;
      Promise.resolve().then(() => BCV.hub.resolve(phrase, { cs, lane, q: ui.raw })).catch(() => []).then((items) => {
        if (!ui || ui.seq !== seq) return;
        ui.groups.set('Best match', items || []);
        ui.reading = items?.label || '';
        ui.pending -= 1;
        paint();
      });
    }
    for (const [name, fn] of net) {
      // a kind the index answered, but with courses it could not read: Canvas is asked for those alone, and the two lists made one
      const part = ui.local.has(name) ? ix.kinds[name].partial : null;
      const ask = part ? cs.filter((c) => part.has(String(c.id))) : cs;
      Promise.resolve().then(() => fn(q, ask, lane)).catch(() => []).then((items) => {
        if (!ui || ui.seq !== seq) return;
        if (part) ui.groups.set(name, merge(ui.groups.get(name) || [], items || []));
        else if (!ui.local.has(name)) ui.groups.set(name, items || []); // (the index came in meanwhile and answered: its rows stand)
        ui.pending -= 1;
        paint();
      });
    }
  }
  /** A command as a row: its name and what it takes, the line under it; Enter runs one that takes nothing, completes one that does. */
  // (a `quick` command runs on Enter as it stands — /grades opens the overview — and lists what it takes once a space follows its name)
  const commandRow = (c) => ({ icon: c.icon, title: `/${c.name}${c.takes && !c.quick ? ` ${c.takes}` : ''}`, sub: c.hint, cmd: c, fill: (c.args || c.text) && !c.quick ? `/${c.name} ` : null });
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
    let p = hub.parse(raw);
    if (!p.cmd) { // the list, narrowed by the name so far — or, no command starting so and the words reading as a phrase ("/physics quiz tomorrow"), the phrase found
      const cmds = hub.matchCommands(p.name);
      if (!cmds.length && /\s/.test(raw.trim()) && hub.understand(raw.slice(1), ui.cs || []).natural) p = { cmd: hub.byName('find'), name: 'find', arg: raw.slice(1).trim() };
      else {
        ui.groups = new Map([['Commands', cmds.map(commandRow)]]);
        paint();
        return;
      }
    }
    const cmd = p.cmd;
    const title = cmd.label || cmd.hint;
    ui.cmd = cmd; ui.arg = p.arg;
    if (cmd.text) { // a command that takes words: the one row is the words typed
      ui.groups = new Map([[title, [{ icon: cmd.icon, title: p.arg ? `${cmd.verb || 'Go'}: ${p.arg}` : `Type ${cmd.takes}…`, sub: p.arg ? cmd.hint : `/${cmd.name} ${cmd.takes}`, act: true }]]]);
      paint();
      return;
    }
    if (!cmd.args) { // a command that takes nothing: the one row is the command, Enter runs it
      ui.groups = new Map([[title, [{ icon: cmd.icon, title: `/${cmd.name}`, sub: cmd.hint, act: true }]]]);
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
      ui.groups = new Map([[items?.label || title, items || []]]); // (a phrase's list is headed by the phrase read back)
      paint();
    };
    if (cmd.net && p.arg) ui.timer = setTimeout(go, PAUSE); else go(); // (a list Canvas is asked for waits for the typing to pause; the page's own answer at once)
  }

  // ---- the panel in motion (2.98.65): results change the way Spotlight's do ----------------------------
  // Every row, group title and line carries a key (its group and what it opens). When the panel is drawn
  // again — a letter typed, a source answering — a row still there glides from where it stood to its new
  // place, a new one fades in a few pixels below and rises (one after another, quickly), one gone fades
  // where it stood, and the panel's height eases to its new one; the highlight on the chosen row is one
  // shape that glides from row to row, as the arrows move it or the rows move under it. Reduced motion:
  // drawn at once, as before.
  const EASE = 'cubic-bezier(.32,.72,0,1)';
  const keyOf = (group, it) => `${group}|${it.href || it.url || (it.cmd ? `/${it.cmd.name}` : '') || it.title}`;
  /** Where each keyed piece of the panel stands now, and the panel's height, before it is drawn again. */
  function snapshot(panel) {
    const rows = new Map();
    // (where it is seen, mid-motion included: a row still rising in carries its opacity on into the next draw, not a pop to full)
    for (const el of panel.querySelectorAll(':scope [data-key]')) rows.set(el.dataset.key, { el, r: el.getBoundingClientRect(), o: el.getAnimations().length ? +getComputedStyle(el).opacity : 1 });
    return { h: panel.getBoundingClientRect().height, rows };
  }
  function flip(panel, was) {
    ui.hAnim?.cancel(); // (a height still easing from the last draw: the new one is measured as it truly is)
    const pr = panel.getBoundingClientRect();
    const seen = new Set();
    let entering = 0;
    for (const el of panel.querySelectorAll(':scope [data-key]')) {
      const k = el.dataset.key;
      seen.add(k);
      const old = was.rows.get(k);
      const r = el.getBoundingClientRect();
      if (old) {
        const dx = old.r.left - r.left, dy = old.r.top - r.top; // (a tile in the Files grid moves across as well as down)
        if (Math.abs(dx) > 0.5 || Math.abs(dy) > 0.5 || old.o < 0.99) el.animate([{ opacity: old.o, transform: `translate(${dx}px, ${dy}px)` }, { opacity: 1, transform: 'none' }], { duration: 260, easing: EASE });
      } else {
        el.animate([{ opacity: 0, transform: 'translateY(6px) scale(.985)' }, { opacity: 1, transform: 'none' }], { duration: 210, delay: Math.min(entering++ * 16, 80), easing: 'cubic-bezier(.2,.8,.2,1)', fill: 'backwards' });
      }
    }
    // gone: a likeness of each row and title fades where it stood (not a row any more: nothing finds it, nothing presses it)
    for (const [k, o] of was.rows) {
      if (seen.has(k) || k.startsWith('msg|')) continue;
      const g = o.el.cloneNode(true);
      g.className = k.startsWith('g|') ? 'bcv-omni__gone bcv-omni__gone--title' : `bcv-omni__gone${o.el.classList.contains('bcv-omni__tile') ? ' bcv-omni__tile' : ''}`;
      g.removeAttribute('data-key'); g.removeAttribute('role'); g.removeAttribute('aria-selected'); g.removeAttribute('tabindex');
      g.setAttribute('aria-hidden', 'true');
      Object.assign(g.style, { left: `${o.r.left - pr.left - panel.clientLeft}px`, top: `${o.r.top - pr.top - panel.clientTop + panel.scrollTop}px`, width: `${o.r.width}px`, height: `${o.r.height}px` });
      panel.append(g);
      const fade = g.animate([{ opacity: o.o }, { opacity: 0, transform: 'scale(.98)' }], { duration: 160, easing: 'ease-out', fill: 'forwards' });
      fade.finished.then(() => g.remove(), () => g.remove());
    }
    // the panel's height, from what it was to what it is
    const h1 = pr.height;
    if (Math.abs(h1 - was.h) > 1) {
      panel.style.overflowY = 'hidden';
      const anim = panel.animate([{ height: `${was.h}px` }, { height: `${h1}px` }], { duration: 260, easing: EASE });
      ui.hAnim = anim;
      const done = () => { if (ui && ui.hAnim === anim) { ui.hAnim = null; panel.style.overflowY = ''; } };
      anim.finished.then(done, done);
    }
  }
  function paint() {
    if (!ui) return;
    const panel = ui.panel;
    const items = [];
    const blocks = [];
    const names = ui.mode === 'plain' ? ORDER : [...ui.groups.keys()]; // (a command's list, the kinds, a kind's own things: as they were set)
    const grid = gridMode();
    panel.classList.toggle('is-grid', grid);
    if (grid) { // the kinds found, as chips across the top: one pressed narrows the grid to it (pressed again, all of them)
      const have = new Set(names.flatMap((n) => ui.groups.get(n) || []).map(fileKind));
      if (ui.fkind) have.add(ui.fkind);
      const chips = KINDS.filter(([k]) => have.has(k));
      if (chips.length > 1 || ui.fkind) {
        blocks.push(h('div', { class: 'bcv-omni__fchips', role: 'group', 'aria-label': 'Kinds of file', dataset: { key: 'msg|chips' } }, chips.map(([k, label]) => h('button', {
          type: 'button', class: 'bcv-omni__fchip', 'aria-pressed': ui.fkind === k ? 'true' : 'false', dataset: { kind: k },
          onmousedown: (e) => e.preventDefault(), // (the cursor stays in the box)
          onclick: () => { if (!ui) return; ui.fkind = ui.fkind === k ? null : k; ui.cursor = 0; ui.walk = false; paint(); },
        }, label))));
      }
    }
    for (const name of names) {
      let list = ui.groups.get(name);
      if (grid && list && ui.fkind) list = list.filter((it) => fileKind(it) === ui.fkind);
      if (!list || !list.length) continue;
      // (a tile keeps its key from Recents to the results typed, so it glides to its new place)
      const rows = list.map((it) => { items.push(it); const el = (grid ? tile : row)(it, items.length - 1); el.dataset.key = grid ? keyOf('tile', it) : keyOf(name, it); return el; });
      const title = h('div', { class: 'bcv-omni__gtitle', text: name === 'Best match' && ui.reading ? ui.reading : name, dataset: { key: `g|${name}` } });
      blocks.push(h('div', { class: `bcv-omni__group${grid ? ' bcv-omni__group--grid' : ''}`, dataset: { group: name } }, grid ? [title, h('div', { class: 'bcv-omni__grid' }, rows)] : [title, ...rows]));
    }
    ui.items = items;
    ui.cursor = items.length ? Math.min(Math.max(ui.cursor, 0), items.length - 1) : -1;
    if (!items.length && !ui.pending && ui.mode === 'plain' && ui.q.length < NET_MIN) { panel.hidden = true; return; } // (one letter, nothing on the page that starts with it: nothing to show yet)
    if (!items.length) blocks.push(h('div', { class: 'bcv-omni__empty', dataset: { key: 'msg|empty' }, text: ui.pending ? 'Searching…' : grid && ui.fkind ? `No ${kindLabel(ui.fkind)} ${ui.q ? `for “${ui.input.value.trim()}”` : 'yet'}.` : ui.mode === 'scope' ? (ui.scope?.key === 'work' ? 'Nothing due this week.' : `No ${ui.scope?.label.toLowerCase() || 'results'} yet.`) : ui.mode === 'cmd' && ui.cmd ? (ui.arg ? `Nothing for “${ui.arg}”` : ui.cmd.empty || `Type ${ui.cmd.takes || 'more'}…`) : ui.mode === 'cmd' ? 'No command by that name. /help lists them.' : `Nothing for “${ui.input.value.trim()}”` }));
    else if (ui.pending) blocks.push(h('div', { class: 'bcv-omni__more', dataset: { key: 'msg|more' }, text: 'Searching…' }));
    // (moving: the panel already on show with something in it, at its top; a panel just opened arrives with the pill, or at once)
    const moving = !still() && !panel.hidden && panel.isConnected && panel.childElementCount > 1 && panel.scrollTop < 1;
    const was = moving ? snapshot(panel) : null;
    // (the highlight stays put, so it glides on from where it is; a row still fading out finishes doing so)
    if (!ui.sel) ui.sel = h('div', { class: 'bcv-omni__sel', 'aria-hidden': 'true' });
    for (const c of [...panel.children]) if (c !== ui.sel && !c.classList.contains('bcv-omni__gone')) c.remove();
    if (panel.firstChild !== ui.sel) panel.prepend(ui.sel);
    panel.append(...blocks);
    panel.hidden = false;
    if (moving) flip(panel, was);
    markCursor({ jump: !moving });
  }
  /** A result's row: its tile and words; what can be done with it as small buttons at the right (shown on the row chosen, or under the pointer). */
  function row(it, i) {
    let acts = it.act ? [] : (BCV.hub?.actionsFor?.(it) || []);
    // (a phone keeps every row's actions in view: in a command's list the one the row does itself — Submit under /submit — is left off)
    if (ui.mode === 'cmd' && ui.cmd && BCV.phone?.active?.()) acts = acts.filter((a) => !norm(a.label).startsWith(ui.cmd.name));
    const el = h('div', { class: `bcv-omni__item${it.answer ? ' bcv-omni__item--ans' : ''}`, role: 'option', tabindex: '-1', 'aria-selected': 'false', dataset: { i }, onclick: (e) => { if (e.target.closest?.('.bcv-omni__act')) return; openItem(it, el); } }, [
      h('span', { class: 'bcv-omni__iic', style: it.tint ? { color: it.tint } : null }, U.svg(it.icon, { size: 15, width: 1.9 })),
      h('span', { class: 'bcv-omni__body' }, [h('span', { class: 'bcv-omni__t', text: it.title }), it.sub ? h('span', { class: 'bcv-omni__s', text: it.sub }) : null]),
      acts.length ? h('span', { class: 'bcv-omni__acts' }, acts.map((a) => h('button', { type: 'button', class: 'bcv-omni__act', title: a.label, onclick: (e) => { e.stopPropagation(); act(a, e.currentTarget); } }, [U.svg(a.icon, { size: 12, width: 2 }), a.label]))) : null,
      it.fill ? h('kbd', { class: 'bcv-omni__hint', text: 'Tab', 'aria-hidden': 'true' }) : null,
      it.key ? h('kbd', { class: 'bcv-omni__cmdkey', text: it.key, 'aria-hidden': 'true' }) : null, // (a kind's ⌘1–⌘4)
      it.url ? h('span', { class: 'bcv-omni__ext', title: 'Opens in a new tab' }, U.svg(IC.external, { size: 12, width: 2 })) : null,
    ]);
    return el;
  }
  // ---- the Files grid afloat (2.98.69) ----------------------------------------------------------------
  const KINDS = [['pdf', 'PDF'], ['word', 'Word'], ['slides', 'Slides'], ['sheets', 'Sheets'], ['img', 'Images'], ['video', 'Video'], ['audio', 'Audio'], ['text', 'Text'], ['zip', 'Archives'], ['page', 'Pages'], ['other', 'Other']];
  const kindLabel = (k) => (KINDS.find((x) => x[0] === k) || [k, k])[1];
  const EXT_KIND = {
    pdf: 'pdf', doc: 'word', docx: 'word', rtf: 'word', odt: 'word', pages: 'word', ppt: 'slides', pptx: 'slides', key: 'slides', odp: 'slides',
    xls: 'sheets', xlsx: 'sheets', csv: 'sheets', numbers: 'sheets', ods: 'sheets',
    png: 'img', jpg: 'img', jpeg: 'img', gif: 'img', webp: 'img', heic: 'img', svg: 'img', bmp: 'img', tif: 'img', tiff: 'img',
    mp4: 'video', mov: 'video', m4v: 'video', webm: 'video', avi: 'video', mp3: 'audio', m4a: 'audio', wav: 'audio', aac: 'audio', ogg: 'audio',
    zip: 'zip', rar: 'zip', '7z': 'zip', gz: 'zip', tar: 'zip', txt: 'text', md: 'text',
  };
  const extRx = /\.([a-z0-9]{1,7})$/i;
  const extOf = (f) => (extRx.exec(f.display_name || '') || extRx.exec(f.filename || '') || [])[1] || '';
  /** What a file is, for its tile and the chips: from its name's extension, else its content type; a course page is a page. */
  function fileKind(it) {
    if (!it.file) return 'page';
    const byExt = EXT_KIND[extOf(it.file).toLowerCase()];
    if (byExt) return byExt;
    const ct = String(it.file['content-type'] || it.file.content_type || '').toLowerCase();
    return ct.includes('pdf') ? 'pdf' : ct.startsWith('image/') ? 'img' : ct.startsWith('video/') ? 'video' : ct.startsWith('audio/') ? 'audio' : ct.startsWith('text/') ? 'text' : 'other';
  }
  /** A file (or a page) as a tile in the grid: a page of its kind with its label in the kind's colour — a picture as its own thumbnail — and its name under it. */
  function tile(it, i) {
    const k = fileKind(it);
    const thumb = k === 'img' && it.file?.thumbnail_url ? it.file.thumbnail_url : null;
    const label = it.file ? (extOf(it.file).slice(0, 4).toUpperCase() || 'FILE') : 'PAGE';
    const doc = h('span', { class: `bcv-omni__doc bcv-omni__doc--${k}${thumb ? ' bcv-omni__doc--thumb' : ''}`, 'aria-hidden': 'true' }, [
      thumb ? h('img', { src: thumb, alt: '', loading: 'lazy', decoding: 'async', draggable: 'false', onerror: (e) => { e.currentTarget.remove(); doc.classList.remove('bcv-omni__doc--thumb'); } }) : null, // (no thumbnail to be had: the page of its kind)
      h('span', { class: 'bcv-omni__docext', text: label }),
    ]);
    const el = h('div', { class: 'bcv-omni__item bcv-omni__tile', role: 'option', tabindex: '-1', 'aria-selected': 'false', 'aria-label': it.sub ? `${it.title}, ${it.sub}` : it.title, title: it.sub ? `${it.title}\n${it.sub}` : it.title, dataset: { i, kind: k }, onclick: () => openItem(it, el) }, [
      doc,
      h('span', { class: 'bcv-omni__tname', text: it.title }),
    ]);
    return el;
  }
  /** The tile above or below the chosen one: in the nearest row that way, the one nearest across (the same one at the grid's edge). */
  function stepRow(down) {
    const tiles = [...ui.panel.querySelectorAll('.bcv-omni__item')];
    const cur = tiles[ui.cursor];
    if (!cur) return 0;
    const cx = cur.offsetLeft + cur.offsetWidth / 2, y = cur.offsetTop;
    let best = ui.cursor, rowY = null, dx = Infinity;
    tiles.forEach((t, i) => {
      const ty = t.offsetTop;
      if (down ? ty <= y + 4 : ty >= y - 4) return; // (this row, or the wrong way)
      if (rowY !== null && (down ? ty > rowY + 4 : ty < rowY - 4)) return; // (a row past the nearest one)
      const d = Math.abs(t.offsetLeft + t.offsetWidth / 2 - cx);
      if (rowY === null || (down ? ty < rowY - 4 : ty > rowY + 4) || d < dx) { rowY = ty; dx = d; best = i; }
    });
    return best;
  }
  function markCursor({ scroll = false, jump = false } = {}) {
    if (!ui) return;
    ui.panel.querySelectorAll('.bcv-omni__item').forEach((el, i) => { const on = i === ui.cursor; el.classList.toggle('is-cur', on); el.setAttribute('aria-selected', on ? 'true' : 'false'); });
    const cur = ui.panel.querySelector('.bcv-omni__item.is-cur');
    if (cur && scroll) cur.scrollIntoView({ block: 'nearest' });
    // the highlight: one shape behind the chosen row, gliding to it (at once when the panel has just opened)
    const sel = ui.sel;
    if (!sel || !sel.isConnected) return;
    ui.panel.classList.toggle('has-sel', !!cur);
    if (!cur) { sel.classList.remove('is-on'); return; }
    const snap = jump || still() || !sel.classList.contains('is-on');
    if (snap) sel.classList.add('is-jump');
    Object.assign(sel.style, { transform: `translate(${cur.offsetLeft}px, ${cur.offsetTop}px)`, width: `${cur.offsetWidth}px`, height: `${cur.offsetHeight}px` });
    sel.classList.add('is-on');
    if (snap) { void sel.offsetWidth; sel.classList.remove('is-jump'); }
  }
  function close() {
    if (!ui) return;
    clearTimeout(ui.timer);
    ui.panel.hidden = true;
    ui.cursor = -1;
    ui.seq += 1; // (a source still answering is answering a search that is closed: it paints nothing)
    ui.pending = 0;
  }
  /** The panel closed for good — a row chosen, a command run: on a phone the keyboard goes with it (the box keeps its words). */
  function done() {
    close();
    if (ui && BCV.phone?.active?.()) ui.input.blur();
    if (afloat()) unfloat(); // (a row chosen: the box goes home)
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
    if (it.scope) { setScope(it.scope); return; } // (a kind: the box narrows to it)
    if (ui.mode === 'cmd' && ui.cmd) { runCommand(ui.cmd, it.act ? null : it, from); return; }
    if (it.cmd) { if (it.fill) fill(it.fill); else runCommand(it.cmd, null, from); return; }
    done();
    if (it.url) window.open(it.url, '_blank', 'noopener');
    else if (it.run) it.run(from);
    else if (it.href) app.go(it.href);
  }
  function runCommand(cmd, item, from) {
    if (!ui || !BCV.hub) return;
    const ctx = { q: ui.arg, cs: ui.cs || [], lane: limiter(LANES), close: done, fill, from, app: ui.app };
    try { const r = cmd.run(item, ctx); if (r && typeof r.catch === 'function') r.catch((e) => U.toast(`${cmd.name} failed: ${e?.message || e}`, { error: true })); } catch (e) { U.toast(`${cmd.name} failed: ${e?.message || e}`, { error: true }); }
  }
  function act(a, from) {
    done();
    try { const r = a.run(from); if (r && typeof r.catch === 'function') r.catch((e) => U.toast(`${a.label} failed: ${e?.message || e}`, { error: true })); } catch (e) { U.toast(`${a.label} failed: ${e?.message || e}`, { error: true }); }
  }
  const caretAtEnd = () => { try { return ui.input.selectionStart === null || ui.input.selectionStart === ui.input.value.length; } catch { return true; } };
  function onKey(e) {
    if (!ui) return;
    if (afloat() && (e.metaKey || e.ctrlKey) && !e.altKey && !e.shiftKey && /^[1-4]$/.test(e.key)) { e.preventDefault(); setScope(SCOPES[Number(e.key) - 1]); return; } // ⌘1–⌘4: a kind
    if (afloat() && e.key === 'Backspace' && !ui.input.value && ui.scope) { e.preventDefault(); clearScope(); return; } // (an empty box: the kind goes)
    if (e.key === 'Escape') {
      if (!ui.panel.hidden && ui.mode !== 'scopes' && ui.mode !== 'scope') { close(); e.preventDefault(); e.stopPropagation(); } // (the panel goes, the words stay: a search field would clear itself)
      else if (afloat()) { e.preventDefault(); e.stopPropagation(); unfloat(); } // (then the box goes home, the kind with it; Backspace alone drops the kind)
      else if (ui.scope) clearScope();
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
    if (gridMode() && /^Arrow(Left|Right|Up|Down)$/.test(e.key)) { // the grid: ↑ ↓ between its rows, ← → along them
      const side = e.key === 'ArrowLeft' || e.key === 'ArrowRight';
      if (side && ui.input.value && !ui.walk) return; // (← → are the caret's while words are typed, until ↓ goes into the grid)
      e.preventDefault();
      ui.walk = true;
      ui.cursor = side ? Math.min(ui.items.length - 1, Math.max(0, ui.cursor + (e.key === 'ArrowRight' ? 1 : -1))) : stepRow(e.key === 'ArrowDown');
      markCursor({ scroll: true });
      return;
    }
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

  /** The box, for the Dashboard's header row (and the phone's Today, under its title): the glyph, the field, the "/" hint, and the panel under it. */
  function field(app) {
    if (spot) settle(); // (a box built afresh while the last was afloat: the old box and its palette go)
    if (docked) { docked.remove(); docked = null; } // (one box at a time: the phone's Today builds its own, and the bar's is built again on the way back)
    const phone = !!BCV.phone?.active?.();
    const input = h('input', { type: 'search', class: 'bcv-omni__in', id: 'bcv-omni', placeholder: phone ? 'Search, or type / for a command' : 'Search everything', autocomplete: 'off', autocapitalize: 'off', autocorrect: 'off', enterkeyhint: 'go', spellcheck: 'false', 'aria-label': 'Search everything: courses, assignments, pages, files, people and Wikipedia — or type / for a command', 'aria-controls': 'bcv-omni-panel', 'aria-autocomplete': 'list' });
    const panel = h('div', { class: 'bcv-omni__panel', id: 'bcv-omni-panel', role: 'listbox', 'aria-label': 'Results' });
    panel.hidden = true;
    // the Wikipedia switch, inside the box at its right where the "/" hint sits: shown while the box has
    // the cursor (the hint goes as it comes), Wikipedia's articles among the results or not, kept in the settings
    const wikiOn = app?.state?.settings?.search?.wikipedia !== false;
    const wikiSw = U.switchEl(wikiOn, (next) => {
      if (!ui) return;
      ui.wiki = next;
      BCV.settings?.update?.({ search: { wikipedia: next } }).catch(() => {});
      if (app?.state?.settings) app.state.settings.search = { ...(app.state.settings.search || {}), wikipedia: next };
      if (!ui.panel.hidden && ui.mode === 'plain' && ui.q.length >= NET_MIN) run(ui.input.value); // (the search on show, asked again with or without it)
      else { ui.items = []; ui.groups = new Map(); ui.q = ''; } // (results kept from before: dropped, so a focus does not bring them back as they were)
    }, 'Wikipedia results');
    wikiSw.id = 'bcv-omni-wiki';
    const wiki = h('span', { class: 'bcv-omni__wiki' }, [h('span', { class: 'bcv-omni__wikit', text: 'Wikipedia', onclick: () => wikiSw.click() }), wikiSw]);
    wiki.addEventListener('pointerdown', (e) => e.preventDefault()); // (the press leaves the cursor in the box: Safari gives a pressed button no focus, and the switch is only there while the box has it)
    const chip = h('span', { class: 'bcv-omni__chip', id: 'bcv-omni-chip' }); // (the kind chosen, afloat)
    chip.hidden = true;
    const box = h('div', { class: 'bcv-omni__box', id: 'bcv-omni-box' }, [
      h('span', { class: 'bcv-omni__ic', 'aria-hidden': 'true' }, U.svg(IC.search, { size: 15, width: 2 })),
      chip,
      input,
      wiki,
      h('kbd', { class: 'bcv-omni__key', text: '/', 'aria-hidden': 'true' }),
    ]);
    const root = h('div', { class: 'bcv-omni', id: 'bcv-omni-root' }, [box, panel]);
    ui = { app, root, input, panel, chip, scope: null, fkind: null, walk: false, placeholder: input.placeholder, ariaLabel: input.getAttribute('aria-label'), wiki: wikiOn, seq: 0, q: '', raw: '', groups: new Map(), pending: 0, cursor: -1, items: [], timer: 0, mode: 'plain', cmd: null, arg: '', cs: null, reading: '' };
    // the highlight is measured from the chosen row; afloat, the rows are first drawn while the palette still eases from the
    // header's width to its own, so a change of the panel's width puts the highlight back on its row (its height is left
    // alone: it eases as the rows change, and the highlight glides on its own then)
    widthWatch?.disconnect();
    if (self.ResizeObserver) {
      let lastW = 0;
      widthWatch = new ResizeObserver((entries) => {
        const w = Math.round(entries[0]?.contentRect.width || 0);
        if (w === lastW) return;
        lastW = w;
        if (ui?.panel === panel && !panel.hidden && w > 0) markCursor({ jump: true });
      });
      widthWatch.observe(panel);
    }
    input.addEventListener('input', () => { if (ui) run(input.value); });
    input.addEventListener('focus', () => {
      if (!ui) return;
      warm().catch(() => {}); // (what the starred courses hold, kept from now on: every letter after this answered at once)
      if ((!spot || spot.folding) && canFloat()) { float(); return; } // (the box lifts to the middle of the window — a press on it as it folds back lifts it again; float() puts the cursor back and paints)
      hubReady().then(() => { if (ui && !ui.panel.hidden && ui.mode === 'plain') paint(); }).catch(() => {}); // (the rows' actions, once the hub is here)
      if (ui.items.length && ui.q === norm(input.value) && ui.panel.hidden) { ui.cursor = Math.max(0, ui.cursor); ui.panel.hidden = false; markCursor({ jump: true }); }
    });
    input.addEventListener('keydown', onKey);
    panel.addEventListener('keydown', onActKey);
    // one highlight (2.98.98): the row a moving mouse comes onto becomes the chosen row — the highlight glides to it, and
    // Enter takes it; a mouse resting while the rows change under it (a letter typed, the arrows scrolling the list)
    // moves nothing, so the first result stays chosen as you type
    let ptAt = null;
    panel.addEventListener('pointermove', (e) => {
      if (e.pointerType !== 'mouse' || !ui || ui.panel !== panel) return;
      if (ptAt && ptAt.x === e.clientX && ptAt.y === e.clientY) return;
      ptAt = { x: e.clientX, y: e.clientY };
      const el = e.target.closest?.('.bcv-omni__item');
      if (!el) return;
      const i = [...panel.querySelectorAll('.bcv-omni__item')].indexOf(el);
      if (i < 0 || i === ui.cursor) return;
      ui.cursor = i;
      markCursor();
    }, { passive: true });
    // (summoned from another screen: summon() lands the words typed on the way once the Dashboard's draw resolves)
    return root;
  }
  /** The box in the widgets' bar, at its left (2.98.70): built once for the desktop shell and kept across screens (it lives
   *  outside the app's root, beside the bar); built again after the phone's Today, or the Dashboard, had one of its own.
   *  Each screen's draw asks. On the Dashboard (2.98.71) the box stands where it always did, in the header between the
   *  title and the view switcher (dashboard.js builds it): the bar's is not shown there. */
  function dock(app, route = null) {
    if (BCV.phone?.active?.()) return;
    let host = document.getElementById('bcv-topsearch');
    if (!host) { host = h('div', { id: 'bcv-topsearch', class: 'bcv-topsearch' }); document.body.append(host); }
    const dash = route?.screen === 'dashboard';
    host.hidden = dash; // (the Dashboard's own box takes over as its draw lands)
    if (dash) return;
    if (docked && ui?.root === docked && docked.isConnected) return; // (in the bar — or afloat from it)
    const root = field(app);
    host.replaceChildren(root);
    docked = root;
  }
  // a press anywhere else closes the panel; "/" (or ⌘K, Ctrl+K) from anywhere on the page puts the cursor in the box — on another screen, the Dashboard comes up with the box focused and whatever is typed meanwhile kept
  document.addEventListener('pointerdown', (e) => {
    if (!ui || !ui.root.isConnected) return;
    if (afloat()) { if (!spot.pal.contains(e.target)) unfloat(); return; } // (a press anywhere else puts the box home — and lands where it was pressed: the veil takes no pointer)
    if (!ui.root.contains(e.target)) close();
  }, true);
  const summonable = () => {
    const app = BCV.app;
    const r = app?.state?.route;
    if (!app || !r || !document.getElementById('bcv-app') || BCV.phone?.active?.()) return false;
    if (r.screen === 'native' || r.params?.get?.('bcv') === 'native' || app.state.quizOpen || document.documentElement.classList.contains('bcv-quiz')) return false;
    return !document.querySelector('.bcv-sheet-ov, .bcv-viewer-ov, #bcv-setup, .bcv-menu');
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
    // the box in the bar (or the phone's on Today), where it is shown and nothing is over the page — a sheet, a quiz
    if (ui && ui.input.isConnected && ui.root.getClientRects().length && (BCV.phone?.active?.() || summonable())) { e.preventDefault(); ui.input.focus(); ui.input.select(); return; }
    if (!summonable() || (k && typing)) return;
    e.preventDefault();
    summon();
  });

  BCV.search = { field, dock, close, summon, float, unfloat, afloat, find };
})();
