/* Settings: every control commits on change and flashes Saved once the write has resolved (an
 * error lands in the same slot). Grade preferences and the course list belong to the Canvas site
 * last used (the page records it), read here with the browser's own Canvas session. */
(async function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const S = BCV.settings;
  const { h } = BCV.utils;
  const $ = (id) => document.getElementById(id);
  const send = (msg) => new Promise((resolve) => {
    try {
      const p = api.runtime.sendMessage(msg);
      if (p && typeof p.then === 'function') p.then(resolve, () => resolve(null));
      else resolve(p ?? null);
    } catch {
      resolve(null);
    }
  });

  const NAV = [
    ['general', 'General', 'M12 3l7 4v6c0 4-3 7-7 8-4-1-7-4-7-8V7z'],
    ['courses', 'Courses & targets', 'M5 4h13v16H5zM5 17h13M9 8h5'],
    ['grades', 'Grades', 'M4 19h16M7 16V9M12 16V5M17 16v-4'],
    ['smart', 'Smart panel', 'M12 3l1.9 4.1L18 9l-4.1 1.9L12 15l-1.9-4.1L6 9l4.1-1.9z'],
    ['appearance', 'Appearance', 'M12 3a9 9 0 100 18c1.1 0 2-.9 2-2 0-1.5 1-2 2-2h1a4 4 0 004-4c0-5-4.5-10-9-10z'],
    ['sites', 'Canvas sites', 'M12 3a9 9 0 100 18 9 9 0 000-18zM3 12h18M12 3a15 15 0 010 18a15 15 0 010-18'],
    ['data', 'Data & about', 'M4 7c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3zM4 7v10c0 1.7 3.6 3 8 3s8-1.3 8-3V7'],
  ];
  const LETTERS = ['C', 'B', 'B+', 'A-', 'A', 'A+']; // target letters, lowest on the left (saved as the letter the Grades page reads)
  const OLD_SCALE = ['A', 'A-', 'B+', 'B', 'B-', 'C+', 'C', 'C-', 'D', 'F']; // targets saved before A+ existed were indices into this
  const targetLetter = (t) => (Number.isInteger(t) ? OLD_SCALE[t] : typeof t === 'string' ? t.replace(/−/g, '-') : null) || 'A';
  const gpa2 = (n) => (Number.isFinite(n) ? n : 0).toFixed(2);

  let settings = await S.get();
  try {
    $('version').textContent = `Version ${api.runtime.getManifest().version}`;
  } catch {
    /* ignore */
  }

  // ---- appearance of this page: the extension's choice, else the system's -------------------------
  const mq = window.matchMedia?.('(prefers-color-scheme: dark)');
  const paint = () => {
    const mode = settings.appearance?.darkMode || 'system';
    const dark = mode === 'on' || (mode === 'system' && !!mq?.matches);
    document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
  };
  const onScheme = () => { if ((settings.appearance?.darkMode || 'system') === 'system') paint(); };
  const listenScheme = () => { // only while the theme is "system"
    mq?.removeEventListener?.('change', onScheme);
    if ((settings.appearance?.darkMode || 'system') === 'system') mq?.addEventListener?.('change', onScheme);
  };
  paint();
  listenScheme();

  // ---- saved pill ---------------------------------------------------------------------------------
  let savedTimer = null;
  const flash = (text = 'Saved', err = false) => {
    const el = $('saved');
    $('savedText').textContent = text;
    el.classList.toggle('is-err', err);
    el.hidden = false;
    clearTimeout(savedTimer);
    savedTimer = setTimeout(() => { el.hidden = true; }, err ? 3200 : 1400);
  };
  const save = async (patch) => {
    try {
      settings = await S.update(patch);
      flash();
    } catch (e) {
      flash(`Not saved: ${e?.message || e}`, true);
    }
    paintAll();
  };
  const debounce = (fn, ms = 400) => { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; };

  // ---- the site whose preferences and courses this page edits ------------------------------------
  const site = { host: '', origin: '' };
  const prefKey = () => `prefs:${site.host}`;
  const readPrefs = async () => { const all = await api.storage.local.get(prefKey()); return (all && all[prefKey()]) || {}; };
  const setPref = async (key, value) => {
    if (!site.host) return;
    const prefs = await readPrefs();
    if (value === null || value === undefined) delete prefs[key]; else prefs[key] = value;
    await api.storage.local.set({ [prefKey()]: prefs });
  };
  const savePref = async (key, value) => {
    try {
      await setPref(key, value);
      flash();
    } catch (e) {
      flash(`Not saved: ${e?.message || e}`, true);
    }
  };
  async function findSite() {
    try {
      const all = await api.storage.local.get(null);
      const last = all['site:last'];
      if (last?.host && last?.origin) { site.host = last.host; site.origin = last.origin; return; }
      const hosts = Object.keys(all).filter((k) => k.startsWith('prefs:')).map((k) => k.slice(6));
      const first = hosts[0] || (settings.domains || []).map((d) => { try { return new URL(d).host; } catch { return ''; } }).find(Boolean);
      if (first) { site.host = first; site.origin = /^localhost|^127\./.test(first) ? `http://${first}` : `https://${first}`; }
    } catch {
      /* no site yet */
    }
  }
  await findSite();
  const canvasGet = (path) => fetch(`${site.origin}${path}`, { credentials: 'include', headers: { Accept: 'application/json' } }).then(async (r) => {
    const text = await r.text();
    if (!r.ok) { const e = new Error(`HTTP ${r.status}`); e.status = r.status; throw e; }
    return JSON.parse(text.replace(/^while\(1\);/, ''));
  });
  let csrf = null;
  const csrfToken = async () => { // Canvas wants the page's token on every write
    if (csrf) return csrf;
    const html = await fetch(`${site.origin}/`, { credentials: 'include' }).then((r) => r.text());
    csrf = html.match(/<meta name="csrf-token" content="([^"]+)"/)?.[1] || '';
    return csrf;
  };
  const canvasWrite = async (method, path) => {
    const r = await fetch(`${site.origin}${path}`, { method, credentials: 'include', headers: { Accept: 'application/json', 'x-csrf-token': await csrfToken() } });
    if (!r.ok) throw new Error(`HTTP ${r.status}`);
  };

  // ---- shell: nav, search, title, status ---------------------------------------------------------
  const nav = $('nav');
  for (const [key, label, icon] of NAV) {
    const tile = h('span', { class: 'navlink__tile' });
    tile.innerHTML = `<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${icon}"/></svg>`;
    nav.append(h('button', { type: 'button', class: 'navlink', dataset: { section: key }, onclick: () => { history.replaceState(null, '', `#${key}`); showSection(key); } }, [tile, h('span', { class: 'navlink__label', text: label }), h('span', { class: 'navlink__dot', id: `dot-${key}`, hidden: true })]));
  }
  function showSection(id) {
    let sec = document.getElementById(id);
    if (!sec || !sec.classList.contains('section')) sec = document.querySelector('.section');
    document.querySelectorAll('.section').forEach((s) => s.classList.toggle('is-active', s === sec));
    document.querySelectorAll('.navlink').forEach((a) => a.classList.toggle('is-active', a.dataset.section === sec.id));
    $('title').innerHTML = sec.dataset.title;
    $('subtitle').textContent = sec.dataset.sub;
    sec.style.animation = 'none';
    void sec.offsetWidth; // restart the entrance
    sec.style.animation = '';
    if (sec.id === 'courses') loadCourses();
    if (sec.id === 'data') renderStats();
  }
  $('query').addEventListener('input', () => {
    const q = $('query').value.trim().toLowerCase();
    let firstShown = null;
    for (const a of document.querySelectorAll('.navlink')) {
      const sec = document.getElementById(a.dataset.section);
      const hay = `${a.textContent} ${sec.dataset.words} ${sec.textContent}`.toLowerCase();
      const on = !q || hay.includes(q);
      a.hidden = !on;
      if (on && !firstShown) firstShown = a.dataset.section;
    }
    const active = document.querySelector('.section.is-active');
    if (q && firstShown && (!active || document.querySelector(`.navlink[data-section="${active.id}"]`).hidden)) showSection(firstShown);
  });
  const keyCount = () => (settings.smart.claudeKey?.trim() ? 1 : 0) + (settings.smart.openaiKey?.trim() ? 1 : 0);
  function paintStatus() {
    const n = (settings.domains || []).length + 1;
    const on = keyCount() > 0;
    $('status').classList.toggle('is-on', on);
    $('statusText').textContent = on ? `${n} ${n === 1 ? 'site' : 'sites'} · smart panel on` : `${n} ${n === 1 ? 'site' : 'sites'}`;
    $('dot-smart').hidden = on;
  }

  // ---- switches, segs -----------------------------------------------------------------------------
  const setSwitch = (el, on) => { el.classList.toggle('is-on', !!on); el.setAttribute('aria-checked', on ? 'true' : 'false'); };
  const onSwitch = (el, fn) => el.addEventListener('click', () => fn(!el.classList.contains('is-on')));
  const setSeg = (el, value) => [...el.querySelectorAll('button')].forEach((b) => b.classList.toggle('is-on', b.dataset.value === String(value)));
  const onSeg = (el, fn) => el.addEventListener('click', (e) => { const b = e.target.closest('button'); if (b) fn(b.dataset.value); });

  // ---- General --------------------------------------------------------------------------------------
  onSwitch($('skin'), (on) => save({ appearance: { skin: on } }));
  const openOnCanvas = async (param) => {
    const msg = $('generalMsg');
    if (!site.origin) {
      msg.hidden = false;
      msg.textContent = 'Open your Canvas once so Simpl Courses knows the site, then come back here.';
      await api.tabs.create({ url: api.runtime.getURL('setup/setup.html') }).catch(() => {});
      return;
    }
    if (settings.appearance.skin === false) await save({ appearance: { skin: true } });
    try {
      await api.tabs.create({ url: `${site.origin}/?bcv=${param}` });
    } catch {
      location.href = `${site.origin}/?bcv=${param}`;
    }
  };
  $('runTour').addEventListener('click', () => openOnCanvas('tour'));
  $('openSetup').addEventListener('click', () => openOnCanvas('setup'));

  // ---- Courses & targets ------------------------------------------------------------------------------
  const courses = { list: null, shown: new Set(), targets: {}, loading: false };
  async function loadCourses() {
    if (courses.list || courses.loading) return;
    courses.loading = true;
    const label = $('shownLabel');
    const empty = $('coursesEmpty');
    if (!site.origin) {
      label.textContent = 'No Canvas site yet';
      empty.textContent = 'Open your Canvas once so Simpl Courses knows the site, then come back here.';
      courses.loading = false;
      return;
    }
    try {
      const [all, favs, colors, prefs] = await Promise.all([
        canvasGet('/api/v1/courses?enrollment_state=active&include[]=term&include[]=favorites&per_page=100'),
        canvasGet('/api/v1/users/self/favorites/courses?per_page=100').catch(() => []),
        canvasGet('/api/v1/users/self/colors').then((r) => r?.custom_colors || {}).catch(() => ({})),
        readPrefs(),
      ]);
      const now = Date.now();
      const fallback = ['#34c759', '#30b0c7', '#ff2d55', '#c8901c', '#ff9500', '#5856d6', '#af52de', '#0a84ff'];
      const favIds = new Set((Array.isArray(favs) ? favs : []).map((c) => String(c.id)));
      const list = [];
      const seen = new Set();
      for (const c of Array.isArray(all) ? all : []) {
        const id = String(c.id);
        if (seen.has(id) || c.access_restricted_by_date || (c.workflow_state && c.workflow_state !== 'available')) continue;
        const end = c.term?.end_at || c.end_at;
        if (end && new Date(end).getTime() < now) continue;
        seen.add(id);
        list.push({ id, code: c.course_code || c.name, name: c.name, color: colors[`course_${id}`] || fallback[list.length % fallback.length], favorite: !!c.is_favorite || favIds.has(id) });
      }
      courses.list = list;
      const starred = list.filter((c) => c.favorite).map((c) => c.id);
      courses.shown = new Set(starred.length ? starred : list.map((c) => c.id)); // nothing starred: Canvas shows every course
      courses.targets = (prefs.gradeTargets && typeof prefs.gradeTargets === 'object') ? { ...prefs.gradeTargets } : {};
      drawCourses();
    } catch (e) {
      label.textContent = 'The courses could not be read';
      empty.textContent = e?.status === 401 || e?.status === 403 ? `Sign in to ${site.host} in this browser, then reload this page.` : `Open ${site.host} once in this browser, then reload this page.`;
    }
    courses.loading = false;
  }
  const setShown = (c, on) => {
    const next = new Set(courses.shown);
    if (on) next.add(c.id); else next.delete(c.id);
    if (!next.size) { flash('Keep at least one course shown', true); drawCourses(); return; }
    writeShown(next);
  };
  function drawCourses() {
    const list = courses.list;
    const wrap = $('courseList');
    $('shownLabel').textContent = `${courses.shown.size} of ${list.length} courses shown`;
    const all = courses.shown.size === list.length;
    $('selectAll').hidden = !list.length;
    $('selectAll').textContent = all ? 'Hide all' : 'Show all';
    wrap.replaceChildren(...(list.length ? list.map((c, i) => {
      const on = courses.shown.has(c.id);
      const cur = targetLetter(courses.targets[c.id]);
      const seg = h('div', { class: 'seg seg--letters' }, LETTERS.map((letter) => h('button', { type: 'button', class: letter === cur ? 'is-on' : '', dataset: { value: letter }, text: letter })));
      onSeg(seg, async (v) => {
        courses.targets[c.id] = v;
        setSeg(seg, v);
        await savePref('gradeTargets', courses.targets);
      });
      const sw = h('button', { type: 'button', class: `switch switch--sm ${on ? 'is-on' : ''}`, role: 'switch', 'aria-checked': on ? 'true' : 'false', 'aria-label': `Show ${c.name} everywhere` }, h('span', { class: 'switch__knob' }));
      onSwitch(sw, (v) => setShown(c, v));
      const row = h('div', { class: `course ${on ? '' : 'is-off'}`, dataset: { course: c.id }, style: { animationDelay: `${Math.min(i * 45, 300)}ms` } }, [
        h('span', { class: 'course__dot', style: { background: c.color } }),
        h('span', { class: 'course__body' }, [h('span', { class: 'course__code', text: c.code }), h('span', { class: 'course__name', text: c.name })]),
        seg, sw,
      ]);
      return row;
    }) : [h('div', { class: 'empty', text: 'No active courses. Between terms? Canvas lists nothing active right now.' })]));
  }
  /** Shown = a Canvas favourite (the one list every screen follows). Written through Canvas, then redrawn. */
  async function writeShown(next) {
    const changes = courses.list.filter((c) => c.favorite !== next.has(c.id));
    try {
      for (const c of changes) {
        await canvasWrite(next.has(c.id) ? 'POST' : 'DELETE', `/api/v1/users/self/favorites/courses/${c.id}`);
        c.favorite = next.has(c.id);
      }
      courses.shown = next;
      flash();
    } catch (e) {
      flash(`Not saved: ${e?.message || e}`, true);
    }
    drawCourses();
  }
  $('selectAll').addEventListener('click', () => {
    const all = courses.shown.size === courses.list.length;
    if (all) { flash('Keep at least one course shown', true); return; }
    writeShown(new Set(courses.list.map((c) => c.id)));
  });

  // ---- Grades ----------------------------------------------------------------------------------------
  const grades = { goal: 0, tracking: null, whatIf: true, snaps: [] };
  const fmtDate = (iso) => { const d = new Date(`${iso}T12:00:00`); return Number.isNaN(d.getTime()) ? iso : d.toLocaleDateString(undefined, { month: 'long', day: 'numeric' }); };
  async function loadGrades() {
    const p = await readPrefs();
    grades.goal = Number.isFinite(p.gpaGoal) ? p.gpaGoal : 0;
    grades.tracking = p.gpaTracking && typeof p.gpaTracking === 'object' && (p.gpaTracking.since || Number.isFinite(p.gpaTracking.priorGpa)) ? p.gpaTracking : null;
    grades.whatIf = p.whatIfScores !== false;
    grades.snaps = Array.isArray(p.gpaSnapshots) ? p.gpaSnapshots : [];
    paintGrades();
  }
  function paintGrades() {
    setSwitch($('tracking'), !!grades.tracking);
    setSwitch($('whatIf'), grades.whatIf);
    $('gpaGoal').textContent = gpa2(grades.goal);
    const n = grades.snaps.length;
    const since = grades.tracking?.since || grades.snaps[0]?.date;
    $('historyLabel').textContent = grades.tracking ? (n ? `${n} ${n === 1 ? 'day' : 'days'} recorded` : 'Recording from today') : (n ? 'History paused' : 'No history yet');
    $('historyNote').textContent = grades.tracking ? (since ? `Since ${fmtDate(since)}` : '') : (n ? 'Existing snapshots kept.' : 'Turn tracking on to keep one snapshot a day.');
    $('exportCsv').disabled = !n;
  }
  onSwitch($('tracking'), async (on) => {
    grades.tracking = on ? (grades.tracking || { priorGpa: null, priorCourses: 0, since: new Date().toISOString().slice(0, 10) }) : null;
    paintGrades();
    await savePref('gpaTracking', grades.tracking);
  });
  onSwitch($('whatIf'), async (on) => { grades.whatIf = on; paintGrades(); await savePref('whatIfScores', on); });
  const goalTo = debounce((v) => savePref('gpaGoal', v), 300);
  $('goalDown').addEventListener('click', () => { grades.goal = Math.max(0, Math.round((grades.goal - 0.05) * 100) / 100); paintGrades(); goalTo(grades.goal); });
  $('goalUp').addEventListener('click', () => { grades.goal = Math.min(4, Math.round((grades.goal + 0.05) * 100) / 100); paintGrades(); goalTo(grades.goal); });
  $('exportCsv').addEventListener('click', () => {
    const lines = ['date,term_gpa', ...grades.snaps.map((s) => `${s.date},${Number.isFinite(s.gpa) ? s.gpa.toFixed(3) : ''}`)];
    download(`simpl-courses-gpa-${site.host || 'canvas'}.csv`, lines.join('\n'), 'text/csv');
  });
  function download(name, text, type) {
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([text], { type }));
    a.download = name;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  }
  if (site.host) loadGrades();

  // ---- Smart panel ----------------------------------------------------------------------------------
  const TEXT = [['claudeKey', 'smart.claudeKey'], ['openaiKey', 'smart.openaiKey'], ['claudeModel', 'smart.claudeModel'], ['openaiModel', 'smart.openaiModel'], ['siteName', 'appearance.siteName'], ['logoUrl', 'appearance.logoUrl']];
  const getPath = (obj, path) => path.split('.').reduce((o, k) => (o == null ? undefined : o[k]), obj);
  const patchFor = (path, value) => path.split('.').reverse().reduce((acc, k) => ({ [k]: acc }), value);
  for (const [id, path] of TEXT) {
    const el = $(id);
    const commit = () => {
      const value = el.value.trim();
      if (id === 'logoUrl' && value && !/^https:\/\//i.test(value)) { flash('The logo must be an https address', true); return; }
      if (value === (getPath(settings, path) ?? '')) return;
      save(patchFor(path, value));
    };
    el.addEventListener('input', debounce(commit, 400));
    el.addEventListener('change', commit);
  }
  document.querySelectorAll('[data-reveal]').forEach((btn) => btn.addEventListener('click', () => {
    const input = $(btn.dataset.reveal);
    const show = input.type === 'password';
    input.type = show ? 'text' : 'password';
    btn.textContent = show ? 'Hide' : 'Show';
  }));
  async function test(provider, inputId, resultId, btnId) {
    const key = $(inputId).value.trim();
    const out = $(resultId);
    const btn = $(btnId);
    btn.disabled = true;
    out.className = 'result';
    out.textContent = 'Testing…';
    await save(patchFor(provider === 'openai' ? 'smart.openaiKey' : 'smart.claudeKey', key));
    const r = await send({ type: 'testKey', provider, key });
    out.textContent = r?.message || 'No response from the extension.';
    out.className = `result ${r?.ok ? 'is-ok' : 'is-err'}`;
    btn.disabled = false;
  }
  $('testClaude').addEventListener('click', () => test('claude', 'claudeKey', 'claudeResult', 'testClaude'));
  $('testOpenAI').addEventListener('click', () => test('openai', 'openaiKey', 'openaiResult', 'testOpenAI'));
  onSeg($('depth'), (v) => save({ smart: { depth: v } }));
  onSeg($('preferred'), (v) => save({ smart: { preferred: v } }));
  onSwitch($('includePageContext'), (on) => save({ smart: { includePageContext: on } }));
  onSwitch($('persistChat'), (on) => save({ smart: { persistChat: on } }));
  function paintSmart() {
    const c = !!settings.smart.claudeKey?.trim();
    const o = !!settings.smart.openaiKey?.trim();
    $('claudePill').textContent = c ? 'Connected' : 'No key';
    $('claudePill').classList.toggle('is-on', c);
    $('openaiPill').textContent = o ? 'Connected' : 'No key';
    $('openaiPill').classList.toggle('is-on', o);
    setSeg($('depth'), settings.smart.depth || 'balanced');
    setSeg($('preferred'), settings.smart.preferred || 'claude');
    setSwitch($('includePageContext'), settings.smart.includePageContext);
    setSwitch($('persistChat'), settings.smart.persistChat);
    const provider = S.resolveProvider(settings.smart);
    $('smartStatus').textContent = provider
      ? `The smart panel uses ${S.providerLabel(provider)} (${S.modelFor(settings.smart, provider)})${c && o ? ' because both keys are set and it is your preferred provider' : ''}. Requests go straight from this browser to the provider with your key.`
      : 'The smart panel stays quiet until you add a key. Requests go straight from this browser to the provider with your key; nothing passes through any other server.';
  }

  // ---- Appearance ----------------------------------------------------------------------------------
  $('themes').addEventListener('click', (e) => {
    const b = e.target.closest('.theme');
    if (b) save({ appearance: { darkMode: b.dataset.value } });
  });

  // ---- Canvas sites ---------------------------------------------------------------------------------
  const normaliseHost = (raw) => {
    let s = String(raw || '').trim().toLowerCase();
    if (!s) return null;
    if (!/^https?:\/\//.test(s)) s = `https://${s}`;
    try {
      const u = new URL(s);
      return u.host.includes('.') || /^localhost(:\d+)?$/.test(u.host) ? u.origin : null;
    } catch {
      return null;
    }
  };
  $('newDomain').addEventListener('input', () => { $('addDomain').disabled = !normaliseHost($('newDomain').value); });
  $('newDomain').addEventListener('keydown', (e) => { if (e.key === 'Enter' && !$('addDomain').disabled) $('addDomain').click(); });
  $('addDomain').addEventListener('click', async () => {
    const origin = normaliseHost($('newDomain').value);
    const msg = $('domainMsg');
    if (!origin) return;
    if (/\.instructure\.com$/i.test(new URL(origin).hostname)) {
      msg.textContent = `${new URL(origin).hostname} is on already: every *.instructure.com site is built in.`;
      return;
    }
    msg.textContent = 'Asking for permission…';
    let granted = false;
    try {
      granted = await api.permissions.request({ origins: [`${origin}/*`] }); // from the click
    } catch (e) {
      msg.textContent = `Permission request failed: ${e?.message || e}`;
      return;
    }
    if (!granted) { msg.textContent = 'Permission was not granted.'; return; }
    const r = await send({ type: 'registerDomain', origin });
    if (!r?.ok) { msg.textContent = r?.message || 'Could not enable that site.'; return; }
    settings = await S.get();
    $('newDomain').value = '';
    $('addDomain').disabled = true;
    msg.textContent = `Enabled on ${origin}. Reload that tab.`;
    flash();
    paintAll();
  });
  function renderDomains() {
    const globe = (color) => { const s = h('span'); s.innerHTML = `<svg viewBox="0 0 24 24" width="15" height="15" fill="none" stroke="${color}" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a15 15 0 010 18a15 15 0 010-18"/></svg>`; return s.firstChild; };
    const rows = [h('div', { class: 'site' }, [globe('var(--ink3)'), h('span', { class: 'site__host', text: '*.instructure.com' }), h('span', { class: 'pill', text: 'Built in' })])];
    for (const origin of settings.domains || []) {
      rows.push(h('div', { class: 'site' }, [
        globe('#34c759'),
        h('span', { class: 'site__host', text: origin.replace(/^https?:\/\//, '') }),
        h('button', { type: 'button', class: 'site__rm', text: 'Remove', onclick: async () => {
          await send({ type: 'unregisterDomain', origin });
          try { await api.permissions.remove({ origins: [`${origin}/*`] }); } catch { /* not granted */ }
          settings = await S.get();
          flash('Removed');
          paintAll();
        } }),
      ]));
    }
    rows.forEach((r, i) => { r.style.animationDelay = `${Math.min(i * 45, 300)}ms`; });
    $('domains').replaceChildren(...rows);
  }

  // ---- Data & about ----------------------------------------------------------------------------------
  const fmtBytes = (n) => (n >= 1048576 ? `${(n / 1048576).toFixed(1)} MB` : n >= 1024 ? `${Math.round(n / 1024)} KB` : `${n} B`);
  async function renderStats() {
    let cached = 0;
    let snaps = 0;
    try {
      const all = await api.storage.local.get(null);
      const cacheKeys = Object.keys(all).filter((k) => k.startsWith('cache:'));
      try {
        cached = await api.storage.local.getBytesInUse(cacheKeys);
      } catch {
        cached = cacheKeys.reduce((s, k) => s + JSON.stringify(all[k] ?? '').length, 0);
      }
      for (const k of Object.keys(all)) if (k.startsWith('prefs:') && Array.isArray(all[k]?.gpaSnapshots)) snaps += all[k].gpaSnapshots.length;
    } catch {
      /* ignore */
    }
    const keys = keyCount();
    $('dataStats').replaceChildren(
      h('div', { class: 'stat' }, [h('span', { text: 'Cached Canvas data' }), h('b', { text: fmtBytes(cached) })]),
      h('div', { class: 'stat' }, [h('span', { text: 'Grade snapshots' }), h('b', { text: snaps ? `${snaps} ${snaps === 1 ? 'day' : 'days'}` : (grades.tracking ? 'from today' : 'none') })]),
      h('div', { class: 'stat' }, [h('span', { text: 'API keys stored' }), h('b', { text: keys ? `${keys} (local only)` : 'none' })]),
    );
  }
  $('clearCache').addEventListener('click', async () => {
    const all = await api.storage.local.get(null);
    const keys = Object.keys(all).filter((k) => k.startsWith('cache:') || k.startsWith('smart:'));
    await api.storage.local.remove(keys);
    flash(`Cleared ${keys.length} cached entries`);
    renderStats();
  });
  $('exportSettings').addEventListener('click', () => {
    const copy = JSON.parse(JSON.stringify(settings));
    copy.smart.claudeKey = '';
    copy.smart.openaiKey = '';
    download('simpl-courses-settings.json', JSON.stringify(copy, null, 2), 'application/json');
  });
  $('importSettings').addEventListener('click', () => $('importFile').click());
  $('importFile').addEventListener('change', async (e) => {
    const file = e.target.files?.[0];
    if (!file) return;
    try {
      const data = JSON.parse(await file.text());
      if (!data || typeof data !== 'object' || Array.isArray(data)) throw new Error('shape');
      const keep = { smart: { claudeKey: settings.smart.claudeKey, openaiKey: settings.smart.openaiKey } };
      settings = await S.replace(S.deepMerge(data, keep));
      flash('Imported');
      paintAll();
    } catch {
      flash('That file is not a settings export', true);
    }
    e.target.value = '';
  });
  $('resetSettings').addEventListener('click', async () => {
    if (!confirm('Reset everything? Preferences, grade history, the sites you added and your API keys are cleared, and the extension gives up its access to those sites.')) return;
    for (const origin of settings.domains || []) {
      await send({ type: 'unregisterDomain', origin });
      try { await api.permissions.remove({ origins: [`${origin}/*`] }); } catch { /* not granted */ }
    }
    try { await api.storage.local.clear(); } catch { /* ignore */ }
    settings = await S.replace({});
    courses.list = null;
    site.host = '';
    site.origin = '';
    grades.goal = 0; grades.tracking = null; grades.whatIf = true; grades.snaps = [];
    flash('Reset');
    paintAll();
    paintGrades();
    renderStats();
  });

  // ---- paint everything from the settings ----------------------------------------------------------
  function paintAll() {
    setSwitch($('skin'), settings.appearance.skin !== false);
    for (const [id, path] of TEXT) { const el = $(id); if (document.activeElement !== el) el.value = getPath(settings, path) ?? ''; }
    paintSmart();
    [...$('themes').querySelectorAll('.theme')].forEach((b) => b.classList.toggle('is-on', b.dataset.value === (settings.appearance.darkMode || 'system')));
    renderDomains();
    paintStatus();
    paint();
    listenScheme();
  }
  paintAll();
  S.onChange((s) => { settings = s; paintAll(); });
  showSection(location.hash ? location.hash.slice(1) : 'general');
})();
