/* The search hub's commands, quick answers and actions (content/app/search.js draws the box and
 * its panel; this is what the box can DO, loaded the first time the box is focused). A "/" in the
 * box lists the commands; a command takes an argument — an assignment, a file, a tool, a course —
 * chosen from a list that narrows as you type, and acts from the box: /submit opens a small hand-in
 * box over the page (never the assignment's page), /download saves a course file, /convert opens the
 * converter with it, /open launches a campus tool in its own tab, /todo shows what is due, /dark and
 * /light switch the look, /note adds a task for today. A result row carries actions of its own too
 * (an assignment: Submit; a file: Download, Convert; a course: Grades, Files), and a sum typed in
 * the box is worked out on the spot. Nothing here is fetched that the screens do not already fetch. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  if (BCV.hub) return; // (put in twice — the app injects every group at once — the first copy stands)
  const { h, overlayRoot } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = () => BCV.store;
  const C = () => BCV.canvas;
  const app = () => BCV.app;
  const norm = (s) => String(s || '').toLowerCase().trim();
  const hit = (s, q) => !q || norm(s).includes(q);
  const nameOf = (c) => c.nickname || c.code || c.name;
  const PER = 8;
  const dayKey = (d = new Date()) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

  // ---- what the commands read ----------------------------------------------------------------------
  const idsOf = (href) => { const m = /\/courses\/(\d+)\/assignments\/(\d+)/.exec(String(href || '')); return m ? { courseId: m[1], aid: m[2] } : null; };
  /** Work due: the planner's assignments and quizzes with nothing handed in, soonest first (the same three weeks the Dashboard and To Do read, so the answer is already here). */
  async function dueItems({ q = '' } = {}) {
    let items = [];
    try { items = await store().planner(); } catch { items = []; }
    const t = Date.now();
    return (items || [])
      .filter((it) => it.isDue && it.date && !it.submitted && !it.complete && !it.dismissed && (it.date >= t - 14 * 864e5) && hit(`${it.title} ${courseWord(it)}`, q))
      .sort((a, b) => a.date - b.date);
  }
  const dueLabel = (d) => { const n = U.dayDiff(d); return n < 0 ? `was due ${U.fmtShort(d)}` : n === 0 ? `due today ${U.fmtTime(d)}` : n === 1 ? `due tomorrow ${U.fmtTime(d)}` : `due ${U.fmtShort(d)}`; };
  const courseWord = (it) => (it.course ? nameOf(it.course) : it.courseName || ''); // (the planner's items carry the store's course, nickname and all)
  const dueItem = (it) => {
    const ids = idsOf(it.url);
    const canHand = it.type === 'assignment' && !!ids;
    return {
      icon: it.icon || IC.doc, title: it.title, sub: `${courseWord(it) || it.kind} · ${dueLabel(it.date)}`, href: it.url, tint: it.course?.color || null,
      actions: canHand ? [{ label: 'Submit', icon: IC.upload, run: (from) => submitPopover(ids.courseId, ids.aid, { from, title: it.title }) }] : [],
      run: canHand ? (from) => submitPopover(ids.courseId, ids.aid, { from, title: it.title }) : null,
    };
  };
  /** A course's files by name, for every favourite course: what /download and /convert pick from. */
  async function fileItems(q, cs, lane) {
    if (!q) return [];
    const lists = await Promise.all(cs.map((c) => lane(async () => {
      try {
        const rows = await C().get(`/api/v1/courses/${c.id}/files`, { params: { search_term: q, per_page: 20 } });
        return (Array.isArray(rows) ? rows : []).filter((f) => f && hit(`${f.display_name || ''} ${f.filename || ''}`, q)).map((f) => fileItem(f, c));
      } catch { return []; }
    })));
    return lists.flat().slice(0, PER);
  }
  const fmtSize = (n) => (n >= 1048576 ? `${(n / 1048576).toFixed(1)} MB` : n >= 1024 ? `${Math.max(1, Math.round(n / 1024))} KB` : n ? `${n} B` : '');
  function fileItem(f, c) {
    return {
      icon: IC.folder, title: f.display_name || f.filename || 'File', sub: `${nameOf(c)} · ${[fmtSize(f.size), (f['content-type'] || '').split('/')[1]].filter(Boolean).join(' · ') || 'File'}`,
      href: `/courses/${c.id}/files/${f.id}`, file: f, actions: fileActions(f),
    };
  }
  const fileActions = (f) => [
    { label: 'Download', icon: IC.download, run: () => downloadFile(f) },
    { label: 'Convert', icon: IC.convert, run: (from) => convertFile(f, from) },
  ];
  /** The file, saved: Canvas's own download address, the session cookie signing it. */
  function downloadFile(f) {
    if (!f?.url) { U.toast('That file has no download address.', { error: true }); return; }
    const a = h('a', { href: f.url, download: f.filename || f.display_name || 'file', style: { display: 'none' } });
    overlayRoot().append(a);
    a.click();
    setTimeout(() => a.remove(), 1000);
    U.toast(`Downloading ${f.display_name || f.filename}…`);
  }
  /** The file's bytes, brought to the converter as a File of its own. */
  async function convertFile(f, from = null) {
    if (!BCV.tools?.open) return;
    if (!f?.url) { BCV.tools.open('conv', { from, over: true }); return; }
    U.toast(`Fetching ${f.display_name || f.filename}…`);
    try {
      const r = await fetch(f.url, { credentials: 'same-origin' });
      if (!r.ok) throw new Error(`Canvas answered ${r.status}`);
      const blob = await r.blob();
      const file = new File([blob], f.display_name || f.filename || 'file', { type: f['content-type'] || blob.type || '' });
      BCV.tools.open('conv', { files: [file], from, over: true });
    } catch (e) {
      U.toast(`Could not fetch the file: ${e.message}`, { error: true });
    }
  }
  /** Campus tools: every favourite course's external tabs, and what the school added to Canvas's own nav. */
  async function toolItems(q, cs) {
    const out = [];
    const seen = new Set();
    const lists = await Promise.all(cs.map((c) => store().tabs(c.id).then((tabs) => (tabs || []).filter((t) => t.type === 'external' || String(t.id || '').startsWith('context_external_tool')).map((t) => ({ t, c }))).catch(() => [])));
    for (const { t, c } of lists.flat()) {
      const href = (() => { try { return new URL(t.html_url || t.full_url || '', location.origin).pathname; } catch { return ''; } })();
      if (!href || !hit(`${t.label} ${nameOf(c)}`, q)) continue;
      const key = `${t.label}|${href}`;
      if (seen.has(key)) continue;
      seen.add(key);
      out.push({ icon: IC.external, title: t.label, sub: `${nameOf(c)} · Campus tool`, tint: c.color || null, run: (from) => BCV.exttool?.openLink({ title: t.label, href, from }), external: true });
    }
    for (const it of BCV.extras?.items?.() || []) {
      if (it.kind !== 'tool' || !hit(it.label, q)) continue;
      out.push({ icon: IC.external, title: it.label, sub: 'School tool', run: (from) => BCV.exttool?.openLink({ title: it.label, href: it.href, from }), external: true });
    }
    return out.slice(0, PER);
  }
  async function courseItems(q) {
    const cs = await store().courses().catch(() => []);
    return cs.filter((c) => c.state === 'current' && hit(`${c.name} ${c.code} ${c.nickname || ''}`, q)).slice(0, PER)
      .map((c) => ({ icon: IC.book, title: nameOf(c), sub: c.name !== nameOf(c) ? c.name : 'Course', href: `/courses/${c.id}`, tint: c.color || null, actions: courseActions(c) }));
  }
  const courseActions = (c) => [
    { label: 'Grades', icon: IC.chart, run: () => app().go(`/courses/${c.id}/grades`) },
    { label: 'Files', icon: IC.folder, run: () => app().go(`/courses/${c.id}/files`) },
  ];
  async function pageItems(q, cs, lane) {
    if (!q) return [];
    const lists = await Promise.all(cs.map((c) => lane(async () => {
      try {
        const rows = await C().get(`/api/v1/courses/${c.id}/pages`, { params: { search_term: q, per_page: 20 } });
        return (Array.isArray(rows) ? rows : []).filter((p) => hit(p.title, q)).map((p) => ({ icon: IC.page, title: p.title, sub: `${nameOf(c)} · Page`, href: `/courses/${c.id}/pages/${p.url}`, tint: c.color || null }));
      } catch { return []; }
    })));
    return lists.flat().slice(0, PER);
  }
  const toolList = (q, { unpinned = false } = {}) => (BCV.tools?.TOOLS || []).filter((t) => hit(`${t.name} ${t.note || ''}`, q) && (!unpinned || !BCV.tools.pinned?.(t.key)));

  // ---- the hand-in box: an assignment's own submission block, in a small box over the page --------
  /** The assignment's hand-in block (screens/submit.js), in a box over whatever page this is: file,
   *  text or link as the assignment allows, the receipt after, Done closing the box. The box is a
   *  sheet like any other, so a tool opened from the block rises over it and the next screen sweeps
   *  it away; the leave guard (unsent files) goes with it either way. */
  async function submitPopover(courseId, aid, { from = null, title = '' } = {}) {
    const A = app();
    const S = store();
    if (!A || !S || !BCV.screens?.submit) return null;
    document.querySelector('.bcv-hub-ov')?.remove();
    let live = true;
    const ov = h('div', { class: 'bcv-sheet-ov bcv-hub-ov', role: 'dialog', 'aria-label': 'Hand in' });
    const titleEl = U.text('bcv-hub-pop__title bcv-ellip', title || 'Hand in');
    const subEl = U.text('bcv-hub-pop__sub bcv-ellip', 'Loading…');
    const body = U.el('bcv-hub-pop__body', U.loading('rows', 3));
    const gone = () => { if (!live) return; live = false; A.state.submitOpen = false; };
    const leave = () => { gone(); ov.classList.add('is-closing'); setTimeout(() => ov.remove(), 180); };
    const close = () => {
      if (!live) return;
      if (A.state.submitOpen && !window.confirm('Your submission has not been sent yet. Close anyway?\n\nAttached files are dropped; a text entry stays as a draft on this device.')) return;
      leave();
    };
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    ov.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !ov.classList.contains('is-under')) { e.stopPropagation(); close(); } });
    const pop = U.el('bcv-sheet bcv-hub-pop', [
      U.el('bcv-sheet__head bcv-hub-pop__head', [
        h('span', { class: 'bcv-sheet__tile bcv-hub-pop__tile' }, U.svg(IC.upload, { size: 17, stroke: '#fff', width: 2 })),
        U.el('bcv-sheet__titles', [titleEl, subEl]),
        h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
      ]),
      body,
    ]);
    ov.append(pop);
    overlayRoot().append(ov);
    if (from) U.morphFrom(pop, from);
    ov.tabIndex = -1;
    ov.focus({ preventScroll: true });
    U.onGone(ov, gone); // (closed, swept away by the next screen, or a popup taking its place)
    try {
      const course = await S.course(String(courseId));
      if (!live) return null;
      if (!course) throw new Error('the course could not be read');
      subEl.textContent = course.name || '';
      const ctx = { app: A, route: { arg: String(aid), params: new URLSearchParams() }, alive: () => live, dark: A.isDark() };
      const el = await BCV.screens.submit.render(ctx, course, { embed: true, title: 'Hand in', onDone: leave });
      if (!live) return null;
      const name = el.querySelector?.('.bcv-sb__h1')?.textContent;
      if (name && !title) titleEl.textContent = name;
      body.replaceChildren(el);
    } catch (e) {
      if (live) body.replaceChildren(U.errorBox(`This assignment could not be loaded: ${e?.message || e}`));
    }
    return { close, el: ov };
  }

  // ---- the commands ---------------------------------------------------------------------------------
  // name, other names it answers to, the line under it, an icon; `takes` says what the argument
  // is (shown as the placeholder); args(q, ctx) lists what the argument can be, narrowed by q
  // (`net`: Canvas is asked, so the list waits for the typing to pause); `text`: the argument is
  // words, not a pick; run(item, ctx) acts on the one chosen (item null: the command alone, with
  // ctx.q as the words typed). ctx: { q, cs, lane, close, fill, from, app }.
  const go = (href) => (_it, ctx) => { ctx.close(); app().go(href); };
  const COMMANDS = [
    { name: 'submit', aliases: ['handin', 'turnin', 'upload'], hint: 'Hand an assignment in, right here', icon: IC.upload, takes: 'an assignment',
      args: async (q) => (await dueItems({ q })).map(dueItem).filter((it) => it.run).slice(0, PER),
      empty: 'Nothing due that takes a hand-in here.',
      run: (it, ctx) => { if (it?.run) { ctx.close(); it.run(ctx.from); } } },
    { name: 'download', aliases: ['save', 'file'], hint: 'Save a course file to this device', icon: IC.download, takes: 'a file', net: true,
      args: (q, ctx) => fileItems(q, ctx.cs, ctx.lane), empty: 'Type part of a file’s name.',
      run: (it, ctx) => { if (it?.file) { ctx.close(); downloadFile(it.file); } } },
    { name: 'convert', aliases: ['converter'], hint: 'Open the converter with a course file, or one from this device', icon: IC.convert, takes: 'a file', net: true,
      args: async (q, ctx) => [{ icon: IC.convert, title: 'A file from this device…', sub: 'Opens the converter', run: (from) => BCV.tools?.open('conv', { from, over: true }) }, ...(await fileItems(q, ctx.cs, ctx.lane))],
      run: (it, ctx) => { if (!it) return; ctx.close(); if (it.file) convertFile(it.file, ctx.from); else it.run?.(ctx.from); } },
    { name: 'open', aliases: ['launch'], hint: 'Open a campus tool in its own tab', icon: IC.external, takes: 'a campus tool',
      args: (q, ctx) => toolItems(q, ctx.cs), empty: 'No campus tool by that name.',
      run: (it, ctx) => { if (it?.run) { ctx.close(); it.run(ctx.from); } } },
    { name: 'todo', aliases: ['due', 'todo'], hint: 'What is due — or open To Do', icon: IC.check, takes: null,
      args: async (q) => (await dueItems({ q })).slice(0, PER).map(dueItem), empty: 'Nothing due in the next three weeks.',
      run: (it, ctx) => { ctx.close(); if (it?.href) app().go(it.href); else app().go('/#todo'); } },
    { name: 'course', aliases: ['class', 'go'], hint: 'Open one of your courses', icon: IC.book, takes: 'a course',
      args: (q) => courseItems(q), run: (it, ctx) => { if (it?.href) { ctx.close(); app().go(it.href); } } },
    { name: 'page', aliases: ['wiki'], hint: 'Find a page in your courses', icon: IC.page, takes: 'a page', net: true,
      args: (q, ctx) => pageItems(q, ctx.cs, ctx.lane), empty: 'Type part of a page’s title.', run: (it, ctx) => { if (it?.href) { ctx.close(); app().go(it.href); } } },
    { name: 'tool', aliases: ['widget'], hint: 'Open one of Simpl’s tools', icon: IC.tool, takes: 'a tool',
      args: (q) => toolList(q).map((t) => ({ icon: t.icon, title: t.name, sub: t.note, tint: t.color, key: t.key })),
      run: (it, ctx) => { ctx.close(); if (it?.key) BCV.tools?.open(it.key, { from: ctx.from }); else app().go('/#tools'); } },
    { name: 'pin', aliases: [], hint: 'Pin a tool beside the switch', icon: IC.pin, takes: 'a tool',
      args: (q) => toolList(q, { unpinned: true }).map((t) => ({ icon: t.icon, title: t.name, sub: 'Pin it', tint: t.color, key: t.key })), empty: 'Every tool is pinned already.',
      run: async (it, ctx) => { if (!it?.key) return; ctx.close(); await BCV.tools?.pin?.(it.key); U.toast(`${it.title} pinned beside the switch.`); } },
    { name: 'note', aliases: ['task', 'remind'], hint: 'Add a task for today to your To Do', icon: IC.plus, takes: 'what to do', text: true, verb: 'Add',
      run: async (_it, ctx) => {
        const title = String(ctx.q || '').trim();
        if (!title) { U.toast('Type what the task is: /note read chapter 4', { error: true }); return; }
        try { await store().createNote({ title, todoDate: `${dayKey()}T09:00:00` }); ctx.close(); U.toast(`Added to To Do: ${title}`); app().refreshCounts?.(); } catch (e) { U.toast(`Could not add it: ${e.message}`, { error: true }); }
      } },
    { name: 'grades', aliases: ['gpa'], hint: 'Open Grades', icon: IC.chart, takes: null, run: go('/grades') },
    { name: 'calendar', aliases: ['cal'], hint: 'Open the Calendar', icon: IC.cal, takes: 'a view',
      args: (q) => [['today', 'Today', 'The calendar as it was, on today'], ['week', 'Week', 'This week, hour by hour'], ['month', 'Month', 'The month at a glance'], ['agenda', 'Agenda', 'What is coming, as a list']].filter(([k, l]) => hit(`${k} ${l}`, q)).map(([k, l, s]) => ({ icon: IC.cal, title: l, sub: s, view: k })),
      run: async (it, ctx) => { ctx.close(); if (it?.view && it.view !== 'today') { try { await store().setPref('calView', it.view); } catch { /* the calendar opens as it was */ } } app().go('/calendar'); } },
    { name: 'inbox', aliases: ['messages', 'mail'], hint: 'Open the Inbox', icon: IC.mail, takes: null, run: go('/conversations') },
    { name: 'courses', aliases: ['classes'], hint: 'Open All Courses', icon: IC.book, takes: null, run: go('/courses') },
    { name: 'groups', aliases: [], hint: 'Open Groups', icon: IC.people, takes: null, run: go('/groups') },
    { name: 'notifications', aliases: ['alerts'], hint: 'Open Notifications', icon: IC.bell, takes: null, run: go('/#notifications') },
    { name: 'tools', aliases: [], hint: 'Open the Tools page', icon: IC.tool, takes: null, run: go('/#tools') },
    { name: 'dark', aliases: ['night'], hint: 'Switch to the dark look', icon: IC.moon, takes: null, run: (_it, ctx) => { ctx.close(); if (!app().isDark()) app().toggleTheme(); else U.toast('The dark look is on already.'); } },
    { name: 'light', aliases: ['day'], hint: 'Switch to the light look', icon: IC.sun, takes: null, run: (_it, ctx) => { ctx.close(); if (app().isDark()) app().toggleTheme(); else U.toast('The light look is on already.'); } },
    { name: 'theme', aliases: ['personalize', 'appearance', 'colour', 'color'], hint: 'Personalize: colours, themes and photos', icon: IC.sparkle, takes: null, run: go('/?bcv=personalize') },
    { name: 'settings', aliases: ['preferences', 'options'], hint: 'Open Simpl’s settings', icon: IC.settings, takes: null, run: (_it, ctx) => { ctx.close(); app().openSettings?.(); } },
    { name: 'whatsnew', aliases: ['changes', 'version', 'new'], hint: 'What changed in this version', icon: IC.star, takes: null, run: (_it, ctx) => { ctx.close(); BCV.whatsnew?.open?.(app(), { manual: true }); } },
    { name: 'setup', aliases: [], hint: 'Run the guided setup again', icon: IC.check, takes: null, run: go('/?bcv=setup') },
    { name: 'history', aliases: ['recent'], hint: 'Pages you visited lately', icon: IC.clock, takes: null, run: (_it, ctx) => { ctx.close(); BCV.extras?.historySheet?.(app()); } },
    { name: 'help', aliases: ['?', 'commands'], hint: 'Every command the box knows', icon: IC.search, takes: null,
      args: () => COMMANDS.filter((c) => c.name !== 'help').map((c) => ({ icon: c.icon, title: `/${c.name}${c.takes ? ` ${c.takes}` : ''}`, sub: c.hint, fill: `/${c.name}${c.args || c.text ? ' ' : ''}` })),
      run: (it, ctx) => { if (it?.fill) ctx.fill(it.fill); } },
  ];
  const byName = (name) => { const s = norm(name); return s ? COMMANDS.find((c) => c.name === s || c.aliases.includes(s)) || null : null; };
  /** The commands a typed name could mean, best first: the name itself, then a name it starts, then
   *  (unless `strict`) a word inside the name or the line under it. */
  function matchCommands(q, { strict = false } = {}) {
    const s = norm(q).replace(/^\//, '');
    const rank = (c) => {
      const names = [c.name, ...c.aliases];
      if (!s) return 3;
      if (names.some((n) => n === s)) return 0;
      if (names.some((n) => n.startsWith(s))) return 1;
      if (!strict && (names.some((n) => n.includes(s)) || norm(c.hint).includes(s))) return 2;
      return -1;
    };
    return COMMANDS.map((c, i) => ({ c, r: rank(c), i })).filter((x) => x.r >= 0).sort((a, b) => a.r - b.r || a.i - b.i).map((x) => x.c);
  }
  /** What the box holds, read as a command: { cmd, name, arg } — cmd once the name is one of the
   *  commands' (or a name it answers to), arg whatever follows the space. */
  function parse(raw) {
    const s = String(raw || '');
    const m = /^\/(\S*)(?:\s+(.*))?$/.exec(s);
    if (!m) return { cmd: null, name: '', arg: '' };
    return { cmd: byName(m[1]), name: m[1], arg: (m[2] || '').trim() };
  }

  // ---- quick answers: a sum worked out, a percentage of a number ------------------------------------
  function quickAnswers(raw) {
    const q = String(raw || '').trim();
    const out = [];
    const pct = /^(\d+(?:\.\d+)?)\s*%\s*(?:of|×|\*)\s*(\d+(?:\.\d+)?)$/i.exec(q);
    if (pct) {
      const v = (Number(pct[1]) / 100) * Number(pct[2]);
      out.push(answer(`${Number(v.toPrecision(10))}`, `${pct[1]}% of ${pct[2]}`));
    } else if (/\d/.test(q) && /[+\-*/^×÷−()]/.test(q) && /^[\d\s.+\-*/^()%×÷−a-z]+$/i.test(q) && !/^[\d.\s-]+$/.test(q)) {
      const v = BCV.tools?.evalSum?.(q);
      if (v !== null && v !== undefined) out.push(answer(v, `${q} =`));
    }
    return out;
  }
  const answer = (value, sub) => ({ icon: IC.calc, title: String(value), sub, answer: true, run: () => { try { navigator.clipboard?.writeText(String(value)); U.toast(`${value} copied.`); } catch { /* the number is on screen */ } } });

  /** The actions a search result carries, by what it is (a file needs its `file`; an assignment its ids in the href; a course its id). */
  function actionsFor(it) {
    if (!it) return [];
    if (it.actions) return it.actions;
    if (it.file) return fileActions(it.file);
    const ids = idsOf(it.href);
    if (ids) return [{ label: 'Submit', icon: IC.upload, run: (from) => submitPopover(ids.courseId, ids.aid, { from, title: it.title }) }];
    const course = /^\/courses\/(\d+)$/.exec(String(it.href || ''));
    if (course) return courseActions({ id: course[1] });
    return [];
  }

  BCV.hub = { COMMANDS, matchCommands, parse, byName, quickAnswers, actionsFor, submitPopover, downloadFile, convertFile, dueItems };
})();
