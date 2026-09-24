/* The search hub's commands, quick answers and actions (content/app/search.js draws the box and
 * its panel; this is what the box can DO, loaded the first time the box is focused). A "/" in the
 * box lists the commands; a command takes an argument — an assignment, a quiz, a file, a tool, a
 * course, a person, a day — chosen from a list that narrows as you type, and acts from the box:
 * /submit opens a small hand-in box over the page (never the assignment's page), /download saves a
 * course file, /convert opens the converter with it, /open launches a tool in its own tab — a
 * course's, the tool behind an assignment (Knewton Alta, listed by its own name as well as by each
 * assignment that opens it), a link written into an assignment's instructions, a module's tool or
 * link, or the school's — /assignment, /quiz, /discussion, /announcement, /file, /page, /module and
 * /people find one of those across the starred courses, /due, /overdue and /todo say what is due,
 * /grades and /calendar take a course or a day, /dark and /light switch the look, /note adds a task
 * for today. The argument can be plain words: "/start today's physics quiz", "/open the lab tool",
 * "/quiz math tomorrow", "/what's due this week" — understand() reads the verb, the kind, the
 * course, the days and the title words, resolve() lists and scores what fits, and the list's title
 * reads the phrase back; a plain search with the same shape (no slash) gets a Best match group. An
 * item a command cannot act on says why on its own line and Enter does the nearest thing (a quiz
 * opens on its page, a tool assignment opens its tool). A result row carries actions of its own too
 * (an assignment: Submit, or Open tool; a file: Download, Convert; a course: Grades, Files; a
 * person: Message), and a sum typed in the box is worked out on the spot. Nothing here is fetched
 * that the screens do not already fetch. */
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
  const hit = (s, q) => { if (!q) return true; const t = norm(s); return norm(q).split(/\s+/).every((w) => t.includes(w)); }; // (every word typed, in any order: "knewton unit 2" finds "Knewton Alta: Unit 2")
  const nameOf = (c) => c.nickname || c.code || c.name;
  const PER = 8;
  const dayKey = (d = new Date()) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  const pathOf = (href) => { try { const u = new URL(href, location.origin); return u.pathname + u.search; } catch { return String(href || ''); } };
  const eachCourse = (cs, lane, read) => Promise.all((cs || []).map((c) => (lane || ((fn) => fn()))(async () => { try { return (await read(c)) || []; } catch { return []; } })));
  const nearestFirst = (list, now = Date.now()) => list.sort((x, y) => Math.abs((x.at ?? Infinity) - now) - Math.abs((y.at ?? Infinity) - now));
  const courseById = (cs, id) => (cs || []).find((c) => String(c.id) === String(id)) || null;

  // ---- what the commands read ----------------------------------------------------------------------
  const idsOf = (href) => { const m = /\/courses\/(\d+)\/assignments\/(\d+)/.exec(String(href || '')); return m ? { courseId: m[1], aid: m[2] } : null; };
  // An assignment's tool, launched the way Canvas's own page launches it (the same route
  // screens/course-detail.js uses), in a tab of its own with the bar over it.
  const isToolAssignment = (a) => (a?.submission_types || []).includes('external_tool');
  const handsIn = (a) => (a?.submission_types || []).some((t) => ['online_upload', 'online_text_entry', 'online_url'].includes(t));
  const toolLaunch = (a, cid) => { const attrs = a.external_tool_tag_attributes || {}; return attrs.url ? `/courses/${cid}/external_tools/retrieve?assignment_id=${a.id}&display=borderless&url=${encodeURIComponent(attrs.url)}` : `/courses/${cid}/assignments/${a.id}`; };
  function launchTool(a, cid, from = null) {
    const url = toolLaunch(a, cid);
    if (BCV.exttool) BCV.exttool.open({ title: a.name, url, newTab: url, from });
    else window.open(url, '_blank', 'noopener');
  }
  const moduleToolUrl = (it, cid) => `/courses/${cid}/external_tools/retrieve?display=borderless&url=${encodeURIComponent(it.external_url || '')}`;
  function launchModuleTool(it, cid, from = null) {
    const url = moduleToolUrl(it, cid);
    if (BCV.exttool) BCV.exttool.open({ title: it.title, url, page: it.html_url ? pathOf(it.html_url) : null, from });
    else window.open(url, '_blank', 'noopener');
  }

  /** Work due: the planner's assignments, quizzes and graded discussions with nothing handed in,
   *  soonest first (the same three weeks the Dashboard and To Do read, so the answer is already
   *  here). Each assignment is joined to its course's own record — what it takes: a hand-in here, a
   *  tool, paper — from the list the Dashboard already read. */
  async function dueItems({ q = '', lane = null } = {}) {
    let items = [];
    try { items = await store().planner(); } catch { items = []; }
    const t = Date.now();
    const list = (items || [])
      .filter((it) => it.isDue && it.date && !it.submitted && !it.complete && !it.dismissed && (it.date >= t - 14 * 864e5) && hit(`${it.title} ${courseWord(it)}`, q))
      .sort((a, b) => a.date - b.date);
    await attachAssignments(list, lane);
    return list;
  }
  async function attachAssignments(list, lane) {
    const byCourse = new Map();
    for (const it of list) {
      const ids = it.type === 'assignment' ? idsOf(it.url) : null;
      if (!ids) continue;
      if (!byCourse.has(ids.courseId)) byCourse.set(ids.courseId, []);
      byCourse.get(ids.courseId).push([it, ids.aid]);
    }
    const run = lane || ((fn) => fn());
    await Promise.all([...byCourse].map(([cid, pairs]) => run(async () => {
      let rows = [];
      try { rows = await store().assignments(cid); } catch { rows = []; }
      for (const [it, aid] of pairs) it.a = (rows || []).find((a) => String(a.id) === String(aid)) || null;
    })));
  }
  const dueLabel = (d) => { const n = U.dayDiff(d); return n < 0 ? `was due ${U.fmtShort(d)}` : n === 0 ? `due today ${U.fmtTime(d)}` : n === 1 ? `due tomorrow ${U.fmtTime(d)}` : `due ${U.fmtShort(d)}`; };
  const courseWord = (it) => (it.course ? nameOf(it.course) : it.courseName || ''); // (the planner's items carry the store's course, nickname and all)
  /** A planner item as a row. What it takes says what Enter does: the hand-in box for an assignment
   *  that takes a file, text or a link; the tool for a tool assignment; its page for a quiz or a
   *  discussion (taken and answered there) — and where the box cannot do it, the row's own line says
   *  why, in the same words everywhere. */
  const dueItem = (it) => {
    const ids = idsOf(it.url);
    const a = it.a || null;
    const base = { icon: it.icon || IC.doc, title: it.title, sub: `${courseWord(it) || it.kind} · ${dueLabel(it.date)}`, href: it.url, tint: it.course?.color || null, item: it, course: it.course || null, at: it.date || null, done: !!it.submitted, quiz: it.type === 'quiz' ? it : undefined };
    const why = (text, extra = {}) => ({ ...base, actions: [], ...extra, sub: `${base.sub} · ${text}`, why: text, run: extra.run || (() => app().go(it.url)) }); // (no Submit on the row: the line says what to do instead)
    if (it.type === 'quiz') return why('Take it on its page');
    if (it.type === 'discussion_topic') return why('Reply on its page');
    if (it.type !== 'assignment' || !ids) return why('Opens its page');
    if (a && isToolAssignment(a)) {
      const open = (from) => launchTool(a, ids.courseId, from);
      return why('Opens the tool', { icon: IC.external, run: open, actions: [{ label: 'Open tool', icon: IC.external, run: open }] });
    }
    if (a && a.locked_for_user) return why('Locked');
    if (a && a.unlock_at && U.parse(a.unlock_at) > Date.now()) return why(`Opens ${U.whenShort(U.parse(a.unlock_at))}`);
    if (a && !handsIn(a)) {
      const types = a.submission_types || [];
      return why(types.includes('on_paper') ? 'On paper' : types.some((t) => ['media_recording', 'student_annotation'].includes(t)) ? 'Handed in on its page' : 'Nothing to hand in');
    }
    const submit = (from) => submitPopover(ids.courseId, ids.aid, { from, title: it.title });
    return { ...base, actions: [{ label: 'Submit', icon: IC.upload, run: submit }], run: submit };
  };
  /** A course's files by name, for every favourite course: what /file, /download and /convert pick from. */
  async function fileItems(q, cs, lane, limit = PER) {
    if (!q) return [];
    const lists = await eachCourse(cs, lane, async (c) => {
      const rows = await C().get(`/api/v1/courses/${c.id}/files`, { params: { search_term: q, per_page: 20 } });
      return (Array.isArray(rows) ? rows : []).filter((f) => f && hit(`${f.display_name || ''} ${f.filename || ''}`, q)).map((f) => fileItem(f, c));
    });
    return lists.flat().slice(0, limit);
  }
  const fmtSize = (n) => (n >= 1048576 ? `${(n / 1048576).toFixed(1)} MB` : n >= 1024 ? `${Math.max(1, Math.round(n / 1024))} KB` : n ? `${n} B` : '');
  function fileItem(f, c) {
    return {
      icon: IC.folder, title: f.display_name || f.filename || 'File', sub: `${nameOf(c)} · ${[fmtSize(f.size), (f['content-type'] || '').split('/')[1]].filter(Boolean).join(' · ') || 'File'}`,
      href: `/courses/${c.id}/files/${f.id}`, file: f, course: c, actions: fileActions(f),
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
  /** The file, opened in the viewer over the page (a PDF, an image, a document — as Files opens it). */
  function viewFile(it, from = null) {
    if (BCV.viewer?.open && it.file) BCV.viewer.open(it.file, { context: it.course || null, from });
    else if (it.href) app().go(it.href);
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
  /** Everything that launches as a tool: every favourite course's external tabs, its assignments
   *  that are a tool (Knewton, Mastering, a publisher's homework), the tools and the links in its
   *  modules, the outside links written into an assignment's own instructions (a publisher's site the
   *  assignment sends you to without being a tool assignment itself), and what the school added to
   *  Canvas's own nav. */
  const outsideLinks = (html) => {
    // the links in a description that lead off Canvas, or to a tool launch: [{ url, text }], none of the same-site file or page links
    const out = [];
    const re = /<a\b[^>]*href=["']([^"']+)["'][^>]*>([\s\S]*?)<\/a>/gi;
    let m;
    while ((m = re.exec(String(html || '')))) {
      const url = m[1].replace(/&amp;/g, '&');
      const text = m[2].replace(/<[^>]+>/g, '').replace(/\s+/g, ' ').trim();
      let u = null;
      try { u = new URL(url, location.origin); } catch { continue; }
      if (!/^https?:$/.test(u.protocol)) continue;
      const away = u.host !== location.host;
      const launch = /\/external_tools\//.test(u.pathname);
      if (!away && !launch) continue; // (a file, a page, an assignment of the same site: not a tool)
      if (/^(here|this|link|click here|click)$/i.test(text)) continue;
      out.push({ url: u.href, text: text || u.host.replace(/^www\./, '') });
    }
    return out;
  };
  // The tool behind an assignment, by its own name: the publisher the launch address belongs to when
  // it is one the table knows, else the words before the colon or dash in the assignment's name
  // ("Knewton Alta: Unit 2" → Knewton Alta) unless they only say which unit or week it is. So the
  // tool is listed as a tool, once per course, as well as each assignment that opens it.
  const BRANDS = [
    [/knewton/, 'Knewton Alta'], [/mastering/, 'Mastering'], [/mylab|pearson/, 'Pearson MyLab'], [/webassign/, 'WebAssign'], [/cengage/, 'Cengage'], [/mheducation|mcgraw/, 'McGraw Hill Connect'], [/aleks/, 'ALEKS'],
    [/wiley/, 'WileyPLUS'], [/macmillan/, 'Macmillan Achieve'], [/zybooks/, 'zyBooks'], [/gradescope/, 'Gradescope'], [/turnitin/, 'Turnitin'], [/tophat/, 'Top Hat'], [/perusall/, 'Perusall'], [/packback/, 'Packback'],
    [/edfinity/, 'Edfinity'], [/myopenmath/, 'MyOpenMath'], [/webwork/, 'WeBWorK'], [/hawkes/, 'Hawkes'], [/lumenlearning|ohm\./, 'Lumen OHM'], [/vitalsource/, 'VitalSource'], [/playposit/, 'PlayPosit'], [/panopto/, 'Panopto'],
    [/kaltura/, 'Kaltura'], [/zoom\.us/, 'Zoom'], [/crowdmark/, 'Crowdmark'], [/expertta/, 'Expert TA'], [/labster/, 'Labster'], [/voicethread/, 'VoiceThread'], [/piazza/, 'Piazza'], [/edstem|edunity/, 'Ed'], [/achieve3000|achieve\./, 'Achieve'],
  ];
  const UNIT_WORD = /^(unit|week|wk|chapter|ch|chap|lesson|lecture|lec|module|mod|section|sec|part|day|hw|homework|assignment|quiz|test|exam|lab|midterm|final|project|problem|set|topic|reading|discussion|activity|worksheet|practice|review|extra|bonus|credit|\d+)$/i;
  function toolNameOf(a) {
    let host = '';
    try { host = new URL(a?.external_tool_tag_attributes?.url || '').host.toLowerCase(); } catch { host = ''; }
    const brand = host ? BRANDS.find(([re]) => re.test(host)) : null;
    if (brand) return brand[1];
    const m = /^([^:–—]{2,40}?)\s*(?::|\s[–—-])\s+\S/.exec(String(a?.name || ''));
    if (!m) return null;
    const words = m[1].trim().split(/\s+/);
    return words.length <= 3 && !words.every((w) => UNIT_WORD.test(w)) ? m[1].trim() : null;
  }
  async function toolItems(q, cs, lane, limit = PER) {
    const out = [];
    const seen = new Set();
    const add = (it, key) => { if (seen.has(key)) return; seen.add(key); out.push(it); };
    const [tabLists, aLists, mLists] = await Promise.all([
      eachCourse(cs, null, (c) => store().tabs(c.id).then((tabs) => (tabs || []).filter((t) => t.type === 'external' || String(t.id || '').startsWith('context_external_tool')).map((t) => ({ t, c })))),
      eachCourse(cs, lane, (c) => store().assignments(c.id).then((rows) => (rows || []).filter((a) => isToolAssignment(a) || outsideLinks(a.description).length).map((a) => ({ a, c })))),
      eachCourse(cs, lane, (c) => store().modules(c.id).then((mods) => (mods || []).flatMap((m) => (m.items || []).filter((it) => it.type === 'ExternalTool' || it.type === 'ExternalUrl').map((it) => ({ it, m, c }))))),
    ]);
    const tabNames = new Set();
    for (const { t, c } of tabLists.flat()) {
      const href = (() => { try { return new URL(t.html_url || t.full_url || '', location.origin).pathname; } catch { return ''; } })();
      if (!href) continue;
      tabNames.add(`${c.id}|${norm(t.label)}`);
      if (!hit(`${t.label} ${nameOf(c)}`, q)) continue;
      add({ icon: IC.external, title: t.label, sub: `${nameOf(c)} · Campus tool`, tint: c.color || null, course: c, run: (from) => BCV.exttool?.openLink({ title: t.label, href, from }), external: true }, `${t.label}|${href}`);
    }
    // the tool itself, once per course, opened through the assignment nearest its due date with nothing handed in
    const named = new Map();
    const stand = (a) => { const at = a.due_at ? U.parse(a.due_at)?.getTime() ?? null : null; const now = Date.now(); return [a.submission?.submitted_at ? 1 : 0, at === null ? Infinity : at < now ? now - at + 365 * 864e5 : at - now]; };
    for (const { a, c } of aLists.flat()) {
      if (!isToolAssignment(a)) continue;
      const name = toolNameOf(a);
      if (!name || tabNames.has(`${c.id}|${norm(name)}`)) continue; // (a tool the course's own nav lists is listed there)
      const key = `${c.id}|${norm(name)}`;
      const cur = named.get(key);
      const [d1, t1] = stand(a);
      if (!cur || d1 < cur.stand[0] || (d1 === cur.stand[0] && t1 < cur.stand[1])) named.set(key, { name, c, a, stand: [d1, t1] });
    }
    for (const { name, c, a } of named.values()) {
      if (!hit(`${name} ${nameOf(c)}`, q)) continue;
      const open = (from) => launchTool(a, c.id, from);
      add({
        icon: IC.external, title: name, sub: `${nameOf(c)} · Tool · via ${a.name}`, tint: c.color || null, course: c, at: a.due_at ? U.parse(a.due_at)?.getTime() ?? null : null, done: !!a.submission?.submitted_at, run: open, external: true, assignment: a, tool: true,
        actions: [{ label: 'Open tool', icon: IC.external, run: open }, { label: 'Assignment', icon: IC.doc, run: () => app().go(`/courses/${c.id}/assignments/${a.id}`) }],
      }, `t:${c.id}:${norm(name)}`);
    }
    for (const { a, c } of aLists.flat()) {
      const s = a.submission || {};
      const at = a.due_at ? U.parse(a.due_at)?.getTime() ?? null : null;
      if (isToolAssignment(a)) {
        if (!hit(`${a.name} ${nameOf(c)}`, q)) continue;
        const open = (from) => launchTool(a, c.id, from);
        add({
          icon: IC.external, title: a.name, sub: `${nameOf(c)} · Assignment tool${a.due_at ? ` · ${dueLabel(U.parse(a.due_at))}` : ''}${s.submitted_at ? ' · handed in' : ''}`, tint: c.color || null, course: c, at, done: !!s.submitted_at, run: open, external: true, assignment: a,
          actions: [{ label: 'Open tool', icon: IC.external, run: open }, { label: 'Assignment', icon: IC.doc, run: () => app().go(`/courses/${c.id}/assignments/${a.id}`) }],
        }, `a:${c.id}:${a.id}`);
        continue;
      }
      // a site the assignment's own instructions send you to: opened in its own tab, the assignment a press away
      for (const link of outsideLinks(a.description)) {
        if (!hit(`${link.text} ${a.name} ${nameOf(c)}`, q)) continue;
        const open = (from) => (BCV.exttool ? BCV.exttool.open({ title: link.text, url: link.url, newTab: link.url, from, icon: IC.link }) : window.open(link.url, '_blank', 'noopener'));
        add({
          icon: IC.link, title: link.text, alt: a.name, sub: `${nameOf(c)} · Link in ${a.name}${a.due_at ? ` · ${dueLabel(U.parse(a.due_at))}` : ''}`, tint: c.color || null, course: c, at, done: !!s.submitted_at, run: open, external: true, assignment: a,
          actions: [{ label: 'Open link', icon: IC.link, run: open }, { label: 'Assignment', icon: IC.doc, run: () => app().go(`/courses/${c.id}/assignments/${a.id}`) }],
        }, `l:${c.id}:${a.id}:${link.url}`);
      }
    }
    for (const { it, m, c } of mLists.flat()) {
      if (!hit(`${it.title} ${m.name} ${nameOf(c)}`, q)) continue;
      const row = moduleItemRow(it, m, c);
      add({ ...row, sub: `${nameOf(c)} · ${m.name} · ${it.type === 'ExternalTool' ? 'Module tool' : 'Module link'}`, external: true }, `m:${c.id}:${it.id}`);
    }
    for (const it of BCV.extras?.items?.() || []) {
      if (it.kind !== 'tool' || !hit(it.label, q)) continue;
      add({ icon: IC.external, title: it.label, sub: 'School tool', run: (from) => BCV.exttool?.openLink({ title: it.label, href: it.href, from }), external: true }, `s:${it.href}`);
    }
    return out.slice(0, limit);
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
  // "/course phys files": a trailing tab word opens that tab of the course
  const TABS = { home: '', announcements: '/announcements', assignments: '/assignments', discussions: '/discussion_topics', grades: '/grades', people: '/users', pages: '/pages', files: '/files', quizzes: '/quizzes', modules: '/modules', syllabus: '/assignments/syllabus' };
  function splitTab(q) {
    const words = norm(q).split(/\s+/).filter(Boolean);
    const last = words[words.length - 1] || '';
    const tab = last.length >= 3 ? Object.keys(TABS).find((t) => t === last || t.startsWith(last)) : null;
    return tab ? { q: words.slice(0, -1).join(' '), tab } : { q: norm(q), tab: null };
  }
  async function pageItems(q, cs, lane, limit = PER) {
    if (!q) return [];
    const lists = await eachCourse(cs, lane, async (c) => {
      const rows = await C().get(`/api/v1/courses/${c.id}/pages`, { params: { search_term: q, per_page: 20 } });
      return (Array.isArray(rows) ? rows : []).filter((p) => hit(p.title, q)).map((p) => ({ icon: IC.page, title: p.title, sub: `${nameOf(c)} · Page`, href: `/courses/${c.id}/pages/${p.url}`, tint: c.color || null, course: c }));
    });
    return lists.flat().slice(0, limit);
  }
  /** Every favourite course's assignments: due, where each stands (store.workStatus), a hand-in or its tool from the row. */
  async function assignmentItems(q, cs, lane, limit = PER) {
    const lists = await eachCourse(cs, lane, (c) => store().assignments(c.id).then((rows) => (rows || []).filter((a) => hit(`${a.name} ${nameOf(c)}`, q)).map((a) => assignmentItem(a, c))));
    return nearestFirst(lists.flat()).slice(0, limit);
  }
  function assignmentItem(a, c) {
    const s = a.submission || {};
    const st = store().workStatus(a, s);
    const tool = isToolAssignment(a);
    const actions = [];
    if (tool) actions.push({ label: 'Open tool', icon: IC.external, run: (from) => launchTool(a, c.id, from) });
    else if (handsIn(a) && !s.submitted_at && !s.excused) actions.push({ label: 'Submit', icon: IC.upload, run: (from) => submitPopover(c.id, a.id, { from, title: a.name }) });
    return {
      icon: tool ? IC.external : IC.doc, title: a.name, sub: `${nameOf(c)} · ${a.due_at ? dueLabel(U.parse(a.due_at)) : 'no due date'} · ${st.word}`,
      href: `/courses/${c.id}/assignments/${a.id}`, tint: c.color || null, assignment: a, course: c, actions, at: a.due_at ? U.parse(a.due_at)?.getTime() ?? null : null, done: !!s.submitted_at || !!s.excused,
    };
  }
  /** Every favourite course's quizzes: due, time limit, attempts, and where you stand (through the quiz's assignment). */
  async function quizItems(q, cs, lane, limit = PER) {
    const lists = await eachCourse(cs, lane, async (c) => {
      const [qs, as] = await Promise.all([store().quizzes(c.id), store().assignments(c.id).catch(() => [])]);
      return (qs || []).filter((z) => hit(`${z.title} ${nameOf(c)}`, q)).map((z) => {
        const a = (as || []).find((x) => String(x.id) === String(z.assignment_id) || (x.is_quiz_assignment && String(x.quiz_id) === String(z.id))) || null;
        const st = a ? store().workStatus(a) : null;
        const attempts = z.allowed_attempts === -1 ? 'unlimited attempts' : z.allowed_attempts > 0 ? U.plural(z.allowed_attempts, 'attempt') : null;
        return {
          icon: IC.bolt, title: z.title, tint: c.color || null, href: `/courses/${c.id}/quizzes/${z.id}`, quiz: z, course: c, at: z.due_at ? U.parse(z.due_at)?.getTime() ?? null : null, done: !!(a?.submission?.submitted_at),
          sub: [nameOf(c), z.due_at ? dueLabel(U.parse(z.due_at)) : null, z.time_limit ? `${z.time_limit} min` : null, attempts, st ? st.word : null].filter(Boolean).join(' · '),
        };
      });
    });
    return nearestFirst(lists.flat()).slice(0, limit);
  }
  /** Every favourite course's discussions: replies, what is new, the last reply, points when graded. */
  async function discussionItems(q, cs, lane, limit = PER) {
    const lists = await eachCourse(cs, lane, (c) => store().discussions(c.id).then((rows) => (rows || []).filter((t) => hit(`${t.title} ${nameOf(c)}`, q)).map((t) => ({
      icon: IC.disc, title: t.title, tint: c.color || null, href: t.html_url ? pathOf(t.html_url) : `/courses/${c.id}/discussion_topics/${t.id}`, at: U.parse(t.last_reply_at || t.posted_at)?.getTime() ?? 0, due: t.assignment?.due_at ? U.parse(t.assignment.due_at)?.getTime() ?? null : null, course: c,
      sub: [nameOf(c), t.discussion_subentry_count ? U.plural(t.discussion_subentry_count, 'reply', 'replies') : 'no replies yet', t.unread_count ? `${t.unread_count} new` : null, t.assignment?.points_possible !== null && t.assignment?.points_possible !== undefined ? `${store().fmtPts(t.assignment.points_possible)} pts` : null, t.last_reply_at ? `last reply ${U.fmtRecent(t.last_reply_at)}` : null].filter(Boolean).join(' · '),
    }))));
    return lists.flat().sort((x, y) => (y.at || 0) - (x.at || 0)).slice(0, limit);
  }
  /** The latest announcements across your courses (the feed the Dashboard reads), newest first, unread said so. */
  async function announcementItems(q, limit = PER) {
    const [feed, all] = await Promise.all([store().announcementsFeed().catch(() => []), store().courses().catch(() => [])]);
    return (feed || []).map((a) => {
      const c = courseById(all, String(a.context_code || '').replace(/^course_/, ''));
      return {
        icon: IC.bell, title: a.title, tint: c?.color || null, href: a.html_url ? pathOf(a.html_url) : (c ? `/courses/${c.id}/discussion_topics/${a.id}` : '/'), at: U.parse(a.posted_at)?.getTime() ?? 0, course: c,
        sub: [c ? nameOf(c) : a.context_name || 'Announcement', a.posted_at ? U.fmtRecent(a.posted_at) : null, a.read_state === 'unread' ? 'Unread' : null, a.discussion_subentry_count ? U.plural(a.discussion_subentry_count, 'reply', 'replies') : null].filter(Boolean).join(' · '),
      };
    }).filter((it) => hit(`${it.title} ${it.course ? nameOf(it.course) : ''}`, q)).sort((x, y) => (y.at || 0) - (x.at || 0)).slice(0, limit);
  }
  /** Every favourite course's modules — and, once a name is typed, the items in them. */
  const ITEM_ICON = { Assignment: IC.doc, Quiz: IC.bolt, Discussion: IC.disc, Page: IC.page, File: IC.folder, ExternalUrl: IC.link, ExternalTool: IC.external };
  const reqWord = (r) => ({ must_view: 'view it', must_submit: 'hand it in', must_contribute: 'contribute', must_mark_done: 'mark it done', min_score: `score ${r.min_score ?? ''} or more`.replace('  ', ' ') }[r.type] || r.type);
  const moduleItemHref = (it, c) => {
    const id = it.content_id;
    if (it.type === 'Assignment' && id) return `/courses/${c.id}/assignments/${id}`;
    if (it.type === 'Quiz' && id) return `/courses/${c.id}/quizzes/${id}`;
    if (it.type === 'Discussion' && id) return `/courses/${c.id}/discussion_topics/${id}`;
    if (it.type === 'Page' && it.page_url) return `/courses/${c.id}/pages/${it.page_url}`;
    if (it.type === 'File' && id) return `/courses/${c.id}/files/${id}`;
    return it.html_url ? pathOf(it.html_url) : `/courses/${c.id}/modules`;
  };
  function moduleItemRow(it, m, c) {
    const cd = it.content_details || {};
    const kind = { Assignment: 'Assignment', Quiz: 'Quiz', Discussion: 'Discussion', Page: 'Page', File: 'File', ExternalUrl: 'Link', ExternalTool: 'Tool' }[it.type] || it.type;
    const row = {
      icon: ITEM_ICON[it.type] || IC.doc, title: it.title, tint: c.color || null, href: moduleItemHref(it, c), course: c, at: cd.due_at ? U.parse(cd.due_at)?.getTime() ?? null : null, done: !!it.completion_requirement?.completed,
      sub: [nameOf(c), m.name, kind, cd.due_at ? dueLabel(U.parse(cd.due_at)) : null, it.completion_requirement ? (it.completion_requirement.completed ? 'done' : reqWord(it.completion_requirement)) : null].filter(Boolean).join(' · '),
    };
    if (it.type === 'ExternalTool') { row.icon = IC.external; row.run = (from) => launchModuleTool(it, c.id, from); }
    else if (it.type === 'ExternalUrl') row.run = (from) => (BCV.exttool ? BCV.exttool.open({ title: it.title, url: it.external_url || it.html_url, newTab: it.external_url || it.html_url, from, icon: IC.link }) : window.open(it.external_url || it.html_url, '_blank', 'noopener'));
    return row;
  }
  async function moduleItems(q, cs, lane, limit = PER) {
    const lists = await eachCourse(cs, lane, (c) => store().modules(c.id).then((mods) => (mods || []).flatMap((m) => {
      const out = [];
      const items = (m.items || []).filter((it) => it.type !== 'SubHeader');
      const req = items.filter((it) => it.completion_requirement);
      const done = req.filter((it) => it.completion_requirement.completed).length;
      if (hit(`${m.name} ${nameOf(c)}`, q)) {
        out.push({
          icon: IC.modules, title: m.name, tint: c.color || null, href: `/courses/${c.id}/modules`, module: m, course: c,
          sub: [nameOf(c), m.state === 'locked' ? `Locked${m.unlock_at ? ` until ${U.whenShort(U.parse(m.unlock_at))}` : ''}` : m.state === 'completed' ? 'Completed' : req.length ? `${done} of ${req.length} requirements done` : null, U.plural(items.length, 'item')].filter(Boolean).join(' · '),
        });
      }
      if (q) for (const it of items) if (hit(`${it.title} ${m.name}`, q)) out.push(moduleItemRow(it, m, c));
      return out;
    })));
    return lists.flat().slice(0, limit);
  }
  /** People in your favourite courses (the roster the People tab reads), by name: their role, their pronouns, a message to them. */
  const ROLE = { StudentEnrollment: 'Student', TeacherEnrollment: 'Teacher', TaEnrollment: 'TA', ObserverEnrollment: 'Observer', DesignerEnrollment: 'Designer' };
  function personItem(u, c) {
    const en = (u.enrollments || []).find((e) => String(e.course_id) === String(c.id)) || (u.enrollments || [])[0] || null;
    const role = en ? (en.role && !/Enrollment$/.test(en.role) ? en.role : ROLE[en.type] || ROLE[en.role] || String(en.type || en.role || '').replace(/Enrollment$/, '')) : null;
    return {
      icon: IC.people, title: u.name, tint: c.color || null, href: `/courses/${c.id}/users/${u.id}`, person: u, course: c,
      sub: [nameOf(c), role, u.pronouns].filter(Boolean).join(' · '),
      actions: [messageAction(u.id, u.name)],
    };
  }
  const messageAction = (id, name) => ({ label: 'Message', icon: IC.mail, run: () => app().go(`/conversations?to=${encodeURIComponent(id)}&to_name=${encodeURIComponent(name || '')}`) });
  async function peopleItems(q, cs, lane, limit = PER) {
    if (!q) return [];
    const lists = await eachCourse(cs, lane, (c) => store().people(c.id).then((rows) => (rows || []).filter((u) => u && u.name && hit(`${u.name} ${u.sortable_name || ''} ${u.short_name || ''}`, q)).map((u) => personItem(u, c))));
    return lists.flat().slice(0, limit);
  }
  const toolList = (q, { unpinned = false, pinnedOnly = false } = {}) => (BCV.tools?.TOOLS || []).filter((t) => hit(`${t.name} ${t.note || ''}`, q) && (!unpinned || !BCV.tools.pinned?.(t.key)) && (!pinnedOnly || !!BCV.tools.pinned?.(t.key)));

  // ---- a day from words: today, tomorrow, friday, next week, oct 3, 3 oct, 10/3, 2026-10-03, in 3 days ------
  const DAYS = ['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday'];
  const MONTHS = ['january', 'february', 'march', 'april', 'may', 'june', 'july', 'august', 'september', 'october', 'november', 'december'];
  function parseWhen(raw, now = new Date()) {
    const s = norm(raw).replace(/^(on|the)\s+/, '').replace(/,/g, ' ').replace(/\s+/g, ' ');
    if (!s) return null;
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const plus = (n) => new Date(today.getFullYear(), today.getMonth(), today.getDate() + n);
    const yearFor = (mi, d) => { const y = today.getFullYear(); return new Date(y, mi, d) < plus(-60) ? y + 1 : y; }; // a month two months gone means next year's
    const monthIndex = (w) => (w.length >= 3 ? MONTHS.findIndex((m) => m.startsWith(w)) : -1);
    if (s === 'today' || s === 'now') return today;
    if (s === 'tomorrow' || s === 'tmrw') return plus(1);
    if (s === 'yesterday') return plus(-1);
    if (s === 'next week') return plus(((8 - today.getDay()) % 7) || 7); // the coming Monday
    if (s === 'next month') return new Date(today.getFullYear(), today.getMonth() + 1, 1);
    let m = /^in (\d+) days?$/.exec(s);
    if (m) return plus(Number(m[1]));
    m = /^(next |this )?([a-z]+)$/.exec(s);
    if (m) {
      const w = m[2];
      const di = w.length >= 3 ? DAYS.findIndex((d) => d.startsWith(w)) : -1;
      if (di >= 0) { let n = (di - today.getDay() + 7) % 7; if (n === 0 && m[1] === 'next ') n = 7; return plus(n); }
      const mi = monthIndex(w);
      if (mi >= 0) return new Date(yearFor(mi, 1), mi, 1);
      return null;
    }
    m = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(s);
    if (m) return new Date(+m[1], +m[2] - 1, +m[3]);
    m = /^(\d{1,2})[/.](\d{1,2})(?:[/.](\d{2,4}))?$/.exec(s);
    if (m) { const y = m[3] ? (+m[3] < 100 ? 2000 + +m[3] : +m[3]) : yearFor(+m[1] - 1, +m[2]); return new Date(y, +m[1] - 1, +m[2]); }
    m = /^([a-z]+)\.? (\d{1,2})(?:st|nd|rd|th)?(?: (\d{4}))?$/.exec(s);
    if (m) { const mi = monthIndex(m[1]); if (mi >= 0) return new Date(m[3] ? +m[3] : yearFor(mi, +m[2]), mi, +m[2]); }
    m = /^(\d{1,2})(?:st|nd|rd|th)? ([a-z]+)\.?(?: (\d{4}))?$/.exec(s);
    if (m) { const mi = monthIndex(m[2]); if (mi >= 0) return new Date(m[3] ? +m[3] : yearFor(mi, +m[1]), mi, +m[1]); }
    return null;
  }

  // ---- plain words: "/start today's physics quiz", "what's due tomorrow", "open the lab tool" -------
  // A phrase is read into a verb (start, open, hand in, find), a kind (quiz, assignment, tool, page …),
  // the courses it names (a word of a course's name, code or nickname: "physics" for PHYS 008, "math"
  // for MATH 021), a span of days (today, tomorrow, this week, friday, overdue, "due" alone for the
  // week ahead, a date as /calendar reads one) and the title words left over. The finders above then
  // list everything of that kind in those courses, every row is scored against the phrase, and the
  // best come back under a line that reads the phrase back. Nothing is asked of Canvas that the
  // finders do not already ask.
  const VERB_OF = {};
  for (const [v, ws] of Object.entries({ start: ['start', 'take', 'begin', 'attempt', 'resume', 'do', 'continue'], open: ['open', 'launch', 'run', 'go', 'goto', 'visit', 'view', 'read', 'load'], submit: ['submit', 'handin', 'turnin', 'upload', 'hand', 'turn', 'send'], find: ['find', 'show', 'list', 'search', 'what', 'whats', 'which', 'where', 'see', 'get', 'lookup', 'look', 'check'] })) for (const w of ws) VERB_OF[w] = v;
  const KIND_OF = {};
  for (const [k, ws] of Object.entries({
    quiz: ['quiz', 'quizzes', 'test', 'tests', 'exam', 'exams'],
    assignment: ['assignment', 'assignments', 'hw', 'homework', 'homeworks', 'project', 'projects', 'essay', 'essays', 'paper', 'papers', 'report', 'reports', 'worksheet', 'worksheets', 'pset', 'psets', 'problem', 'problems', 'work'],
    discussion: ['discussion', 'discussions', 'thread', 'threads', 'forum', 'post', 'posts'],
    page: ['page', 'pages', 'notes', 'reading', 'readings'],
    file: ['file', 'files', 'pdf', 'pdfs', 'slides', 'slide', 'handout', 'handouts', 'doc', 'docs'],
    module: ['module', 'modules'],
    tool: ['tool', 'tools', 'sim', 'simulation', 'simulations', 'app', 'site', 'website', 'link', 'links'],
    announcement: ['announcement', 'announcements', 'news'],
    people: ['people', 'person', 'classmate', 'classmates', 'teacher', 'teachers', 'professor', 'prof', 'ta', 'tas', 'instructor', 'student', 'students', 'someone', 'who'],
    grades: ['grade', 'grades', 'gpa', 'score', 'scores', 'marks'],
    calendar: ['calendar', 'schedule', 'agenda'],
  })) for (const w of ws) KIND_OF[w] = k;
  const FILLER = new Set(['the', 'a', 'an', 'my', 'me', 'i', 'im', 'id', 'ill', 'ive', 'for', 'in', 'of', 'to', 'on', 'at', 'from', 'with', 'please', 'pls', 'plz', 'is', 'are', 'was', 'be', 'that', 'this', 'it', 'its', 'and', 'or', 'up', 'out', 'class', 'course', 'courses', 'one', 'ones', 'thing', 'things', 'stuff', 'some', 'any', 'all', 'by', 'before', 's', 'want', 'wanna', 'need', 'gotta', 'lets', 'let', 'can', 'could', 'you', 'your', 'we', 'our', 'into', 'onto', 'about', 'just', 'again', 'back', 'then', 'there', 'here', 'have', 'has', 'do', 'does', 'did', 'got', 'whats', 'what']);
  const KIND_LABEL = { quiz: 'a quiz', assignment: 'an assignment', discussion: 'a discussion', page: 'a page', file: 'a file', module: 'a module', tool: 'a tool', announcement: 'an announcement', people: 'someone', grades: 'grades', calendar: 'the calendar' };
  const VERB_LABEL = { start: 'Start', open: 'Open', submit: 'Hand in', find: 'Find' };
  /** The words of a phrase or a title: lower-case, possessives dropped, split on anything that is not a letter or a digit (a date like 10/3 or 2026-10-03 kept whole). */
  const tokens = (s) => norm(s).replace(/[’']s\b/g, '').replace(/[’'`]/g, '').split(/[^a-z0-9/.\-]+/).flatMap((t) => { const w = t.replace(/^[/.\-]+|[/.\-]+$/g, ''); if (!w) return []; return /^\d+([/.\-]\d+)+$/.test(w) ? [w] : w.split(/[/.\-]+/).filter(Boolean); });
  const isNum = (w) => /^\d+$/.test(w);
  /** Two words that are the one word: the same, the same number (021 is 21), or one the start of the other from three letters ("phys", "physics"). */
  const sameWord = (w, t) => w === t || (isNum(w) && isNum(t) ? Number(w) === Number(t) : !isNum(w) && !isNum(t) && ((w.length >= 3 && t.startsWith(w)) || (t.length >= 3 && w.startsWith(t))));
  const wordHit = (w, toks) => toks.some((t) => sameWord(w, t));
  /** A span of days from a word or two: { lo, hi } in days from today, and the words to read it back. */
  function spanOf(phrase, today = new Date()) {
    const s = phrase.trim();
    const dow = today.getDay();
    const one = (n, label) => ({ lo: n, hi: n, label });
    if (['today', 'tonight', 'now'].includes(s)) return one(0, 'today');
    if (['tomorrow', 'tmrw', 'tmr', 'tomorow'].includes(s)) return one(1, 'tomorrow');
    if (s === 'yesterday') return one(-1, 'yesterday');
    if (s === 'this week' || s === 'week') return { lo: 0, hi: 6 - dow, label: 'this week' };
    if (s === 'next week') return { lo: 7 - dow, hi: 13 - dow, label: 'next week' };
    if (s === 'this weekend' || s === 'weekend') { const sat = (6 - dow + 7) % 7; return dow === 0 ? one(0, 'this weekend') : { lo: sat, hi: sat + 1, label: 'this weekend' }; }
    if (s === 'this month' || s === 'next month') { const y = today.getFullYear(); const mo = today.getMonth(); const left = new Date(y, mo + 1, 0).getDate() - today.getDate(); return s === 'this month' ? { lo: 0, hi: left, label: 'this month' } : { lo: left + 1, hi: left + new Date(y, mo + 2, 0).getDate(), label: 'next month' }; }
    if (['overdue', 'late', 'missing', 'past', 'missed'].includes(s)) return { lo: -365, hi: -1, label: 'overdue' };
    if (['soon', 'upcoming', 'coming'].includes(s)) return { lo: 0, hi: 7, label: 'soon' };
    const m = /^(next |this )?([a-z]+)$/.exec(s);
    if (m) {
      const di = m[2].length >= 3 ? DAYS.findIndex((d) => d.startsWith(m[2])) : -1;
      if (di < 0) return null;
      let n = (di - dow + 7) % 7;
      if (n === 0 && m[1] === 'next ') n = 7;
      return one(n, DAYS[di][0].toUpperCase() + DAYS[di].slice(1));
    }
    if (/\d/.test(s)) { const d = parseWhen(s, today); if (d) return one(U.dayDiff(d, today), U.fmtShort(d)); }
    return null;
  }
  /** The courses a phrase names: a word of a course's name, code or nickname (a word every course shares — the term — names none; a bare number counts only beside a word that matched). */
  function matchCourses(words, cs) {
    const sets = (cs || []).map((c) => ({ id: String(c.id), toks: [...new Set(tokens(`${c.name || ''} ${c.code || ''} ${c.nickname || ''}`))] }));
    if (sets.length >= 2) { const shared = sets[0].toks.filter((t) => sets.every((s) => s.toks.includes(t))); for (const s of sets) s.toks = s.toks.filter((t) => !shared.includes(t)); }
    const alpha = words.filter((w) => !isNum(w));
    const nums = words.filter(isNum);
    const hits = new Map();
    for (const s of sets) {
      const got = alpha.filter((w) => wordHit(w, s.toks));
      if (!got.length) continue;
      for (const n of nums) if (wordHit(n, s.toks)) got.push(n);
      hits.set(s.id, got);
    }
    const best = Math.max(0, ...[...hits.values()].map((g) => g.length));
    const courses = best ? [...hits].filter(([, g]) => g.length === best).map(([id]) => id) : [];
    return { courses, courseWords: [...new Set([...hits.values()].flat())] };
  }
  /** The phrase, read: { verb, kind, courses, courseWords, span, words, natural, strong }. `kind` forces
   *  the kind a command takes (its own kind words are dropped); `verb` is the command's own. `natural`
   *  says the phrase held more than title words — a verb, a span, a course, a kind, a filler — so
   *  the resolver should read it; `strong` says a plain search (no slash) should, which takes more:
   *  a span, or a verb, a kind or a course with something beside it. */
  function understand(raw, cs, { kind: forced = null, verb: given = null } = {}) {
    const ws = tokens(raw);
    const now = new Date();
    let verb = null;
    let kind = forced;
    let span = null;
    let due = false;
    let dropped = false;
    const kindWords = [];
    const rest = [];
    for (let i = 0; i < ws.length; i += 1) {
      const w = ws[i];
      let sp = null;
      let used = 0;
      for (const n of [3, 2, 1]) {
        if (i + n > ws.length) continue;
        const p = ws.slice(i, i + n).join(' ');
        if (n > 1 && !/^(this|next|in)\s|\d/.test(p)) continue; // (two words are a span only as "this week", "next friday", "in 3 days", "oct 3")
        sp = spanOf(p, now);
        if (sp) { used = n; break; }
      }
      if (sp) { span = sp; i += used - 1; continue; }
      if (w === 'due' || w === 'deadline' || w === 'deadlines') { due = true; continue; }
      if (!verb && !rest.length && !kindWords.length && !span && VERB_OF[w]) { verb = VERB_OF[w]; continue; }
      const k = KIND_OF[w];
      if (k && (forced ? k === forced : !kind || k === kind)) { if (!forced) kind = k; kindWords.push(w); continue; }
      if (FILLER.has(w)) { dropped = true; continue; }
      rest.push(w);
    }
    if (due && !span) span = { lo: 0, hi: 7, label: 'due soon' };
    const { courses, courseWords } = matchCourses(rest, cs);
    const words = rest.filter((w) => !courseWords.includes(w));
    verb = given || verb;
    const natural = !!(verb || span || courses.length || kindWords.length || dropped || due);
    const strong = !!(span || (verb && (kind || courses.length || words.length)) || (kind && (courses.length || words.length)) || (courses.length && words.length));
    return { verb, kind, kindWords, courses, courseWords, span, words, natural, strong, raw: String(raw || '') };
  }
  /** A row against the phrase: in a course named +3 (another course: out); a title word +2, a word missed −1; a course's or a kind's word in the title +1; in the span +4, out of it −3, undated −2; done −2 when the verb is start or submit. */
  function score(row, r) {
    let s = 0;
    if (r.courses.length) { if (!row.course || !r.courses.includes(String(row.course.id))) return null; s += 3; }
    const toks = tokens(`${row.title || ''} ${row.alt || ''}`);
    let hits = 0;
    for (const w of r.words) { if (wordHit(w, toks)) { s += 2; hits += 1; } else s -= 1; }
    for (const w of [...r.courseWords, ...r.kindWords]) if (wordHit(w, toks)) s += 1;
    let inSpan = false;
    if (r.span) {
      const at = row.due !== undefined ? row.due : row.at;
      if (at === null || at === undefined) s -= 2;
      else { const n = U.dayDiff(new Date(at)); inSpan = n >= r.span.lo && n <= r.span.hi; s += inSpan ? 4 : -3; }
    }
    if (row.done && (r.verb === 'start' || r.verb === 'submit')) s -= 2;
    return { row, s, hits, inSpan };
  }
  /** What Enter does under the verb: start takes a quiz (its page, one question at a time as the quiz allows) and hands an assignment in or opens its tool; submit hands in. */
  const withVerb = (row, r) => {
    if (r.verb === 'start') {
      if (row.quiz && row.href && !row.done) return { ...row, sub: `${row.sub} · Enter starts it`, run: () => app().go(`${row.href}?bcv=take`) };
      const act = (row.actions || []).find((a) => a.label === 'Submit' || a.label === 'Open tool');
      if (act) return { ...row, run: act.run };
    }
    if (r.verb === 'submit') { const act = (row.actions || []).find((a) => a.label === 'Submit'); if (act) return { ...row, run: act.run }; }
    return row;
  };
  /** The phrase read back, as the group's title: "Start a quiz · in MATH-021-20 · today", "Find work · “lab” · in PHYS-008-01 · this week". */
  function readingLabel(r, cs) {
    const what = r.kind ? KIND_LABEL[r.kind] : r.verb === 'start' ? 'a quiz or an assignment' : r.verb === 'open' ? 'a tool or a page' : r.verb === 'submit' ? 'an assignment' : 'work';
    const names = r.courses.map((id) => courseById(cs, id)).filter(Boolean).map(nameOf);
    const parts = [`${VERB_LABEL[r.verb] || 'Find'} ${what}`];
    if (r.words.length) parts.push(`“${r.words.join(' ')}”`);
    if (names.length) parts.push(`in ${names.slice(0, 2).join(', ')}${names.length > 2 ? ` +${names.length - 2}` : ''}`);
    if (r.span) parts.push(r.span.label);
    return parts.join(' · ');
  }
  const gradeRows = (cs, r) => (r.courses.length ? cs.map((c) => ({ icon: IC.chart, title: `Grades in ${nameOf(c)}`, sub: c.name, href: `/courses/${c.id}/grades`, tint: c.color || null, course: c, actions: [] })) : [{ icon: IC.chart, title: 'Grades overview', sub: 'Every course, and your GPA', href: '/grades', actions: [] }]);
  const calendarRows = (r) => {
    if (!r.span) return [{ icon: IC.cal, title: 'Calendar', sub: 'The calendar as it was', href: '/calendar' }];
    const d = new Date(); d.setDate(d.getDate() + r.span.lo);
    return [{ icon: IC.cal, title: U.fmtLong(d), sub: 'The calendar on that day', at: d.getTime(), run: () => app().go(`/calendar?date=${dayKey(d)}`) }];
  };
  /** The rows for a phrase: everything of its kinds (the kind named, else what the verb takes: start —
   *  quizzes and assignments; open — tools, assignments, modules, pages, files; find — quizzes,
   *  assignments, discussions) in the courses named, each scored; a row of another course is out; with
   *  title words, only rows that hold one; with a span, only rows inside it once any is; best first,
   *  the nearest date between equals; PER at most, each doing what the verb says on Enter; the phrase
   *  read back on the list's `label`. */
  async function resolve(r, ctx) {
    const all = ctx.cs || [];
    const cs = r.courses.length ? all.filter((c) => r.courses.includes(String(c.id))) : all;
    const lane = ctx.lane || null;
    const kinds = r.kind ? [r.kind] : r.verb === 'start' ? ['quiz', 'assignment'] : r.verb === 'open' ? ['tool', 'assignment', 'module', 'page', 'file'] : r.verb === 'submit' ? ['assignment'] : ['quiz', 'assignment', 'discussion'];
    const longest = r.words.slice().sort((a, b) => b.length - a.length)[0] || '';
    const READ = {
      quiz: () => quizItems('', cs, lane, Infinity),
      assignment: () => assignmentItems('', cs, lane, Infinity),
      discussion: () => discussionItems('', cs, lane, Infinity),
      tool: () => toolItems('', cs, lane, Infinity),
      module: () => moduleItems(r.words.join(' '), cs, lane, Infinity),
      page: () => (longest ? pageItems(longest, cs, lane, Infinity) : []),
      file: () => (longest ? fileItems(longest, cs, lane, Infinity) : []),
      announcement: () => announcementItems('', Infinity).then((rows) => rows.filter((it) => !r.courses.length || (it.course && r.courses.includes(String(it.course.id))))),
      people: () => (longest ? peopleItems(longest, cs, lane, Infinity) : []),
      grades: () => gradeRows(cs, r),
      calendar: () => calendarRows(r),
    };
    const lists = await Promise.all(kinds.map((k) => Promise.resolve().then(() => (READ[k] ? READ[k]() : [])).catch(() => [])));
    const seen = new Set();
    let scored = [];
    for (const row of lists.flat()) {
      const key = `${row.course?.id ?? ''}|${norm(row.title)}`; // (a quiz is an assignment too: the quiz row stands for both)
      if (seen.has(key)) continue;
      seen.add(key);
      const x = score(row, r);
      if (x) scored.push(x);
    }
    if (r.words.length) scored = scored.filter((x) => x.hits);
    if (r.span && scored.some((x) => x.inSpan)) scored = scored.filter((x) => x.inSpan);
    const now = Date.now();
    const dist = (row) => { const at = row.due !== undefined ? row.due : row.at; return at === null || at === undefined ? Number.MAX_SAFE_INTEGER : Math.abs(at - now); };
    scored.sort((x, y) => y.s - x.s || dist(x.row) - dist(y.row));
    const rows = scored.slice(0, PER).map((x) => withVerb(x.row, r));
    rows.label = readingLabel(r, all);
    return rows;
  }
  /** A finder that reads a phrase first: an argument with a verb, a span, a course or a filler in it goes to the resolver, a plain name to the finder as before. */
  const nl = (kind, fn) => (q, ctx) => { const r = understand(q, ctx.cs, { kind }); return r.natural ? resolve(r, ctx) : fn(q, ctx); };

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
    const leave = () => { gone(); U.dismiss(ov); }; // (out on its spring, from wherever it had got to)
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
  // words, not a pick; `quick`: Enter on the command's own row runs it with no argument (a space
  // after the name lists what it takes); run(item, ctx) acts on the one chosen (item null: the
  // command alone, with ctx.q as the words typed). ctx: { q, cs, lane, close, fill, from, app }.
  const go = (href) => (_it, ctx) => { ctx.close(); app().go(href); };
  /** Enter on a found row: its own act where it has one (a tool, a link), else its page. */
  const openRow = (it, ctx) => { if (!it) return; ctx.close(); if (it.run) it.run(ctx.from); else if (it.href) app().go(it.href); };
  const viewName = (k) => (k === 'today' ? 'Today' : k[0].toUpperCase() + k.slice(1));
  // (`label` heads the list a command shows — short, since it is a title; `hint` is the line under the command's own row)
  const COMMANDS = [
    { name: 'submit', aliases: ['handin', 'turnin', 'upload'], hint: 'Hand an assignment in, right here', label: 'Hand in', icon: IC.upload, takes: 'an assignment',
      args: async (q, ctx) => (await dueItems({ q, lane: ctx.lane })).map(dueItem).slice(0, PER),
      empty: 'Nothing due that takes a hand-in.',
      run: (it, ctx) => { if (it?.run) { ctx.close(); it.run(ctx.from); } } },
    { name: 'start', aliases: ['take', 'begin', 'attempt', 'resume'], hint: 'Start a quiz or an assignment, in plain words: "/start today’s quiz"', label: 'Start', icon: IC.bolt, takes: 'a quiz or an assignment',
      args: (q, ctx) => resolve(understand(q, ctx.cs, { verb: 'start' }), ctx), empty: 'Nothing to start by those words.', run: openRow },
    { name: 'find', aliases: ['show', 'list', 'what', 'whats', 'which', 'search'], hint: 'Find anything in plain words: "/find lab this week", "/what’s due tomorrow"', label: 'Best match', icon: IC.search, takes: 'anything, in plain words',
      args: (q, ctx) => resolve(understand(q, ctx.cs, { verb: 'find' }), ctx), empty: 'Say what you are after: "quiz tomorrow", "the lab tool".', run: openRow },
    { name: 'assignment', aliases: ['hw', 'homework', 'assign'], hint: 'Find an assignment, hand it in or open its tool', label: 'Assignments', icon: IC.doc, takes: 'an assignment',
      args: nl('assignment', (q, ctx) => assignmentItems(q, ctx.cs, ctx.lane)), empty: 'No assignments in your courses yet.', run: openRow },
    { name: 'quiz', aliases: ['quizzes', 'test', 'exam'], hint: 'Find a quiz: due, time limit, attempts, your score', label: 'Quizzes', icon: IC.bolt, takes: 'a quiz',
      args: nl('quiz', (q, ctx) => quizItems(q, ctx.cs, ctx.lane)), empty: 'No quizzes in your courses yet.', run: openRow },
    { name: 'discussion', aliases: ['disc', 'thread', 'discuss'], hint: 'Find a discussion: replies, what is new, the last reply', label: 'Discussions', icon: IC.disc, takes: 'a discussion',
      args: nl('discussion', (q, ctx) => discussionItems(q, ctx.cs, ctx.lane)), empty: 'No discussions in your courses yet.', run: openRow },
    { name: 'announcement', aliases: ['ann', 'news', 'announcements'], hint: 'The latest announcements, or one by name', label: 'Announcements', icon: IC.bell, takes: 'an announcement',
      args: nl('announcement', (q) => announcementItems(q)), empty: 'No announcements yet.', run: openRow },
    { name: 'download', aliases: ['save'], hint: 'Save a course file to this device', label: 'Save a file', icon: IC.download, takes: 'a file', net: true,
      args: (q, ctx) => fileItems(q, ctx.cs, ctx.lane), empty: 'Type part of a file’s name.',
      run: (it, ctx) => { if (it?.file) { ctx.close(); downloadFile(it.file); } } },
    { name: 'file', aliases: ['files', 'read'], hint: 'Open a course file in the viewer', label: 'Open a file', icon: IC.folder, takes: 'a file', net: true,
      args: (q, ctx) => fileItems(q, ctx.cs, ctx.lane), empty: 'Type part of a file’s name.',
      run: (it, ctx) => { if (it?.file) { ctx.close(); viewFile(it, ctx.from); } } },
    { name: 'convert', aliases: ['converter'], hint: 'Open the converter with a course file, or one from this device', label: 'Convert a file', icon: IC.convert, takes: 'a file', net: true,
      args: async (q, ctx) => [{ icon: IC.convert, title: 'A file from this device…', sub: 'Opens the converter', run: (from) => BCV.tools?.open('conv', { from, over: true }) }, ...(await fileItems(q, ctx.cs, ctx.lane))],
      run: (it, ctx) => { if (!it) return; ctx.close(); if (it.file) convertFile(it.file, ctx.from); else it.run?.(ctx.from); } },
    { name: 'open', aliases: ['launch'], hint: 'Open a tool in its own tab', label: 'Open a tool', icon: IC.external, takes: 'a tool',
      args: nl('tool', (q, ctx) => toolItems(q, ctx.cs, ctx.lane)), empty: 'No tool by that name in your courses.',
      run: (it, ctx) => { if (it?.run) { ctx.close(); it.run(ctx.from); } } },
    { name: 'page', aliases: ['wiki'], hint: 'Find a page in your courses', label: 'Pages', icon: IC.page, takes: 'a page', net: true,
      args: (q, ctx) => pageItems(q, ctx.cs, ctx.lane), empty: 'Type part of a page’s title.', run: (it, ctx) => { if (it?.href) { ctx.close(); app().go(it.href); } } },
    { name: 'module', aliases: ['modules', 'unit'], hint: 'Find a module, or something in one', label: 'Modules', icon: IC.modules, takes: 'a module or an item',
      args: nl('module', (q, ctx) => moduleItems(q, ctx.cs, ctx.lane)), empty: 'No modules in your courses yet.', run: openRow },
    { name: 'people', aliases: ['person', 'who', 'classmate', 'teacher', 'ta'], hint: 'Find someone in your courses: their role, a message to them', label: 'People', icon: IC.people, takes: 'a name', net: true,
      args: (q, ctx) => peopleItems(q, ctx.cs, ctx.lane), empty: 'Type part of a name.', run: openRow },
    { name: 'due', aliases: ['upcoming', 'soon'], hint: 'What is due: today, tomorrow, this week, or by name', label: 'Due', icon: IC.clock, takes: 'today, tomorrow, week or a name',
      args: async (q, ctx) => {
        const word = norm(q);
        const span = word === 'today' ? [0, 0] : word === 'tomorrow' ? [1, 1] : word === 'week' || word === 'this week' ? [0, 6] : null;
        const list = await dueItems({ q: span ? '' : q, lane: ctx.lane });
        return list.filter((it) => { const n = U.dayDiff(it.date); return span ? n >= span[0] && n <= span[1] : n >= 0; }).slice(0, PER).map(dueItem);
      },
      empty: 'Nothing due in the next three weeks.', run: openRow },
    { name: 'overdue', aliases: ['late', 'missing'], hint: 'Past due with nothing handed in', label: 'Overdue', icon: IC.warn, takes: 'a name (or nothing)',
      args: async (q, ctx) => (await dueItems({ q, lane: ctx.lane })).filter((it) => it.date < Date.now()).slice(0, PER).map(dueItem),
      empty: 'Nothing overdue.', run: openRow },
    { name: 'todo', aliases: ['todo', 'tasks'], hint: 'What is due — or open To Do', label: 'To Do', icon: IC.check, takes: null,
      args: async (q, ctx) => (await dueItems({ q, lane: ctx.lane })).slice(0, PER).map(dueItem), empty: 'Nothing due in the next three weeks.',
      run: (it, ctx) => { ctx.close(); if (it?.href) app().go(it.href); else app().go('/#todo'); } },
    { name: 'course', aliases: ['class', 'go'], hint: 'Open a course — or one of its tabs: "/course phys files"', label: 'Courses', icon: IC.book, takes: 'a course, then a tab',
      args: async (q) => { const { q: name, tab } = splitTab(q); return (await courseItems(name)).map((it) => (tab ? { ...it, sub: `${viewName(tab)} of ${it.title}`, href: `${it.href}${TABS[tab]}`, actions: [] } : it)); },
      run: (it, ctx) => { if (it?.href) { ctx.close(); app().go(it.href); } } },
    { name: 'tool', aliases: ['widget'], hint: 'Open one of Simpl’s tools', label: 'Simpl’s tools', icon: IC.tool, takes: 'a tool',
      args: (q) => toolList(q).map((t) => ({ icon: t.icon, title: t.name, sub: t.note, tint: t.color, key: t.key })),
      run: (it, ctx) => { ctx.close(); if (it?.key) BCV.tools?.open(it.key, { from: ctx.from }); else app().go('/#tools'); } },
    { name: 'pin', aliases: [], hint: 'Pin a tool beside the switch', label: 'Pin a tool', icon: IC.pin, takes: 'a tool',
      args: (q) => toolList(q, { unpinned: true }).map((t) => ({ icon: t.icon, title: t.name, sub: 'Pin it', tint: t.color, key: t.key })), empty: 'Every tool is pinned already.',
      run: async (it, ctx) => { if (!it?.key) return; ctx.close(); await BCV.tools?.pin?.(it.key); U.toast(`${it.title} pinned beside the switch.`); } },
    { name: 'unpin', aliases: [], hint: 'Take a tool off the tray beside the switch', label: 'Unpin a tool', icon: IC.pin, takes: 'a pinned tool',
      args: (q) => toolList(q, { pinnedOnly: true }).map((t) => ({ icon: t.icon, title: t.name, sub: 'Unpin it', tint: t.color, key: t.key })), empty: 'Nothing is pinned.',
      run: async (it, ctx) => { if (!it?.key) return; ctx.close(); await BCV.tools?.unpin?.(it.key); U.toast(`${it.title} unpinned.`); } },
    { name: 'note', aliases: ['task', 'remind'], hint: 'Add a task for today to your To Do', label: 'Add a task', icon: IC.plus, takes: 'what to do', text: true, verb: 'Add',
      run: async (_it, ctx) => {
        const title = String(ctx.q || '').trim();
        if (!title) { U.toast('Type what the task is: /note read chapter 4', { error: true }); return; }
        ctx.close(); // (the list goes at once; a search typed while Canvas is still saving the task is not closed under the typist)
        try { await store().createNote({ title, todoDate: `${dayKey()}T09:00:00` }); U.toast(`Added to To Do: ${title}`); app().refreshCounts?.(); } catch (e) { U.toast(`Could not add it: ${e.message}`, { error: true }); }
      } },
    { name: 'grades', aliases: ['gpa', 'grade'], hint: 'Open Grades — or a course’s: "/grades math"', label: 'Grades', icon: IC.chart, takes: 'a course', quick: true,
      args: async (q) => [...(q ? [] : [{ icon: IC.chart, title: 'Grades overview', sub: 'Every course, and your GPA', href: '/grades' }]), ...(await courseItems(q)).map((it) => ({ ...it, icon: IC.chart, sub: `Grades in ${it.title}`, href: `${it.href}/grades`, actions: [] }))],
      run: (it, ctx) => { ctx.close(); app().go(it?.href || '/grades'); } },
    { name: 'calendar', aliases: ['cal'], hint: 'Open the Calendar on a view, or a day: "/calendar friday"', label: 'Calendar', icon: IC.cal, takes: 'a view or a day',
      args: (q) => {
        const views = [['today', 'Today', 'The calendar as it was, on today'], ['week', 'Week', 'This week, hour by hour'], ['month', 'Month', 'The month at a glance'], ['agenda', 'Agenda', 'What is coming, as a list']].filter(([k, l]) => hit(`${k} ${l}`, q)).map(([k, l, s]) => ({ icon: IC.cal, title: l, sub: s, view: k }));
        const when = q && !views.some((v) => v.view === norm(q)) ? parseWhen(q) : null;
        return [...(when ? [{ icon: IC.cal, title: U.fmtLong(when), sub: 'The calendar on that day', date: when }] : []), ...views];
      },
      empty: 'Type a view — week, month, agenda — or a day: tomorrow, friday, oct 3.',
      run: async (it, ctx) => { ctx.close(); if (it?.date) { app().go(`/calendar?date=${dayKey(it.date)}`); return; } if (it?.view && it.view !== 'today') { try { await store().setPref('calView', it.view); } catch { /* the calendar opens as it was */ } } app().go('/calendar'); } },
    { name: 'inbox', aliases: ['messages', 'mail'], hint: 'Open the Inbox', icon: IC.mail, takes: null, run: go('/conversations') },
    { name: 'courses', aliases: ['classes'], hint: 'Open All Courses', icon: IC.book, takes: null, run: go('/courses') },
    { name: 'groups', aliases: [], hint: 'Open Groups', icon: IC.people, takes: null, run: go('/groups') },
    { name: 'notifications', aliases: ['alerts'], hint: 'Open Notifications', icon: IC.bell, takes: null, run: go('/#notifications') },
    { name: 'tools', aliases: [], hint: 'Open the Tools page', icon: IC.tool, takes: null, run: go('/#tools') },
    { name: 'dark', aliases: ['night'], hint: 'Switch to the dark look', icon: IC.moon, takes: null, run: (_it, ctx) => { ctx.close(); if (!app().isDark()) app().toggleTheme(); else U.toast('The dark look is on already.'); } },
    { name: 'light', aliases: ['day'], hint: 'Switch to the light look', icon: IC.sun, takes: null, run: (_it, ctx) => { ctx.close(); if (app().isDark()) app().toggleTheme(); else U.toast('The light look is on already.'); } },
    { name: 'theme', aliases: ['personalize', 'appearance', 'colour', 'color'], hint: 'Personalize: colours, themes and photos', label: 'Personalize', icon: IC.sparkle, takes: null, run: go('/?bcv=personalize') },
    { name: 'settings', aliases: ['preferences', 'options'], hint: 'Open Simpl’s settings', icon: IC.settings, takes: null, run: (_it, ctx) => { ctx.close(); app().openSettings?.(); } },
    { name: 'whatsnew', aliases: ['changes', 'version', 'new'], hint: 'What changed in this version', icon: IC.star, takes: null, run: (_it, ctx) => { ctx.close(); BCV.whatsnew?.open?.(app(), { manual: true }); } },
    { name: 'setup', aliases: [], hint: 'Run the guided setup again', icon: IC.check, takes: null, run: go('/?bcv=setup') },
    { name: 'history', aliases: ['recent'], hint: 'Pages you visited lately', icon: IC.clock, takes: null, run: (_it, ctx) => { ctx.close(); BCV.extras?.historySheet?.(app()); } },
    { name: 'help', aliases: ['?', 'commands'], hint: 'Every command the box knows', label: 'Commands', icon: IC.search, takes: null,
      args: () => COMMANDS.filter((c) => c.name !== 'help').map((c) => ({ icon: c.icon, title: `/${c.name}${c.takes ? ` ${c.takes}` : ''}`, sub: c.hint, fill: `/${c.name}${c.args || c.text ? ' ' : ''}` })),
      run: (it, ctx) => { if (it?.fill) ctx.fill(it.fill); } },
  ];
  const byName = (name) => { const s = norm(name).replace(/[’']s$/, ''); return s ? COMMANDS.find((c) => c.name === s || c.aliases.includes(s)) || null : null; }; // ("/what's due" is /what)
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

  /** The actions a search result carries, by what it is: a file its Download and Convert; an
   *  assignment Submit — or Open tool when it is a tool, nothing when it takes no hand-in; a course
   *  its Grades and Files; a person a Message. */
  function actionsFor(it) {
    if (!it) return [];
    if (it.actions) return it.actions;
    if (it.file) return fileActions(it.file);
    if (it.person?.id) return [messageAction(it.person.id, it.person.name)];
    const ids = idsOf(it.href);
    if (ids) {
      const a = it.assignment || null;
      if (a && isToolAssignment(a)) return [{ label: 'Open tool', icon: IC.external, run: (from) => launchTool(a, ids.courseId, from) }];
      if (a && !handsIn(a)) return [];
      return [{ label: 'Submit', icon: IC.upload, run: (from) => submitPopover(ids.courseId, ids.aid, { from, title: it.title }) }];
    }
    const course = /^\/courses\/(\d+)$/.exec(String(it.href || ''));
    if (course) return courseActions({ id: course[1] });
    return [];
  }

  BCV.hub = { COMMANDS, matchCommands, parse, byName, quickAnswers, actionsFor, submitPopover, downloadFile, convertFile, viewFile, dueItems, parseWhen, launchTool, understand, resolve, readingLabel, toolNameOf };
})();
