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

  // In the Mac app's window (the page over its bridge, Resources/Bridge.js) the app comes first —
  // Safari's word on the extension, updates, open at login — and the two sections that need the
  // browser's Canvas session (courses, grades) are not here: those are set on Canvas's own pages.
  const inApp = !!self.SimplApp;
  const NAV = [
    ...(inApp ? [['app', 'This Mac', 'M4 5h16v11H4zM8 20h8M12 16v4']] : []),
    ['general', 'General', 'M12 3l7 4v6c0 4-3 7-7 8-4-1-7-4-7-8V7z'],
    ...(inApp ? [] : [
      ['courses', 'Courses & targets', 'M5 4h13v16H5zM5 17h13M9 8h5'],
      ['grades', 'Grades', 'M4 19h16M7 16V9M12 16V5M17 16v-4'],
    ]),
    ['appearance', 'Appearance', 'M12 3a9 9 0 100 18c1.1 0 2-.9 2-2 0-1.5 1-2 2-2h1a4 4 0 004-4c0-5-4.5-10-9-10z'],
    ['sites', 'Canvas sites', 'M12 3a9 9 0 100 18 9 9 0 000-18zM3 12h18M12 3a15 15 0 010 18a15 15 0 010-18'],
    ['data', 'Data & about', 'M4 7c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3zM4 7v10c0 1.7 3.6 3 8 3s8-1.3 8-3V7'],
  ];
  const NAV_KEYS = new Set(NAV.map(([k]) => k));
  if (inApp) $('app').hidden = false;
  const LETTERS = ['C', 'B', 'B+', 'A-', 'A', 'A+', 'P/F']; // target letters, lowest on the left (saved as the letter the Grades page reads); P/F: pass/fail, out of the GPA
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
      // and hand it to the open Canvas tabs: a content script cannot count on hearing the storage
      // change itself (Safari often never delivers one written from here)
      await api.runtime.sendMessage({ type: 'pushSettings' }).catch(() => {});
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
  const csrfToken = async () => { // Canvas wants the session's token on every write; the Canvas page remembers it here (this page cannot read the cookie)
    if (csrf) return csrf;
    try {
      const k = `csrf:${site.host}`;
      const all = await api.storage.local.get(k);
      csrf = (all && all[k]) || '';
    } catch {
      csrf = '';
    }
    if (!csrf) { // a Canvas that still prints the token into its pages
      const html = await fetch(`${site.origin}/`, { credentials: 'include' }).then((r) => r.text()).catch(() => '');
      csrf = html.match(/<meta name="csrf-token" content="([^"]+)"/)?.[1] || '';
    }
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
    let sec = NAV_KEYS.has(id) ? document.getElementById(id) : null;
    if (!sec || !sec.classList.contains('section')) sec = document.getElementById(NAV[0][0]);
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
  function paintStatus() {
    const n = (settings.domains || []).length + 1;
    $('status').classList.toggle('is-on', true);
    $('statusText').textContent = `${n} ${n === 1 ? 'site' : 'sites'}`;
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
      msg.textContent = inApp ? 'Open your Canvas in Safari once so Simpl Courses knows the site, then come back here.' : 'Open your Canvas once so Simpl Courses knows the site, then come back here.';
      if (!inApp) await api.tabs.create({ url: api.runtime.getURL('setup/setup.html') }).catch(() => {});
      return;
    }
    if (settings.appearance.skin === false) await save({ appearance: { skin: true } });
    try {
      await api.tabs.create({ url: `${site.origin}/?bcv=${param}` });
    } catch {
      location.href = `${site.origin}/?bcv=${param}`;
    }
  };
  $('runWelcome').addEventListener('click', () => openOnCanvas('welcome'));
  $('openSetup').addEventListener('click', () => openOnCanvas('setup'));

  // ---- This Mac (the app's window only) --------------------------------------------------------------
  if (inApp) {
    const EXT = {
      on: ['Simpl Courses is on in Safari', 'Turn it off under Safari → Settings → Extensions.'],
      off: ['Simpl Courses is off in Safari', 'Tick it under Safari → Settings → Extensions, and open your Canvas: the setup begins there.'],
      missing: ['Safari does not have the extension yet', ''],
      unknown: ['Reading Safari…', ''],
    };
    const when = (t) => (t ? new Date(t * 1000).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' }) : '');
    const paintApp = (st) => {
      const e = st.extension || {};
      const p = st.placement || {};
      const move = e.state === 'missing' && (p.translocated === true || p.inApplications === false); // (the app is where Safari cannot see it: the button is the move)
      const [t, sub] = EXT[e.state] || EXT.unknown;
      $('extTitle').textContent = t;
      $('extSub').textContent = e.state === 'missing' ? (e.detail || '') : sub;
      $('extSteps').hidden = e.state !== 'missing';
      $('moveApp').hidden = !move;
      $('openSafari').hidden = move;
      const u = st.update || {};
      const act = $('updAction');
      act.hidden = true;
      const checked = u.checked ? `Checked at ${when(u.checked)} · checks every hour` : 'Checks every hour.';
      switch (u.state) {
        case 'checking': $('updTitle').textContent = 'Checking for updates…'; $('updSub').textContent = `Version ${st.version}`; break;
        case 'available': $('updTitle').textContent = `Version ${u.available} is ready`; $('updSub').textContent = `You have ${st.version}.${u.automatic === false ? '' : ' It installs by itself in a moment.'}`; act.hidden = false; break;
        case 'downloading': $('updTitle').textContent = 'Downloading the update…'; $('updSub').textContent = `${Math.round((u.progress || 0) * 100)}%`; break;
        case 'installing': $('updTitle').textContent = 'Installing…'; $('updSub').textContent = 'The app opens again by itself.'; break;
        case 'failed': $('updTitle').textContent = 'Could not check for updates'; $('updSub').textContent = u.message || ''; break;
        case 'upToDate': $('updTitle').textContent = `Version ${st.version} is the newest`; $('updSub').textContent = checked; break;
        default: $('updTitle').textContent = `Version ${st.version}`; $('updSub').textContent = checked;
      }
      $('checkUpdates').disabled = ['checking', 'downloading', 'installing'].includes(u.state);
      setSwitch($('autoUpdate'), u.automatic !== false);
      setSwitch($('loginItem'), !!st.loginItem);
      if (document.querySelector('.section.is-active')?.id === 'data') renderStats();
    };
    self.SimplApp.onState(paintApp);
    $('openSafari').addEventListener('click', async () => {
      const r = await self.SimplApp.openSafariSettings().catch(() => null);
      const msg = $('appMsg');
      msg.hidden = !(r && r.ok === false);
      if (r && r.ok === false) msg.textContent = `${r.message || 'Safari could not open its settings.'} Open Safari, then Safari → Settings → Extensions and tick Simpl Courses.`;
    });
    $('moveApp').addEventListener('click', async () => {
      const r = await self.SimplApp.moveToApplications().catch(() => null); // (done, the app opens again from there: no reply comes)
      const msg = $('appMsg');
      msg.hidden = !(r && r.ok === false);
      if (r && r.ok === false) msg.textContent = r.message || 'The app could not move itself. Drag it to the Applications folder in the Finder, then open it again.';
    });
    $('checkUpdates').addEventListener('click', () => { self.SimplApp.checkUpdates(); });
    $('updAction').addEventListener('click', () => { self.SimplApp.installUpdate(); });
    onSwitch($('autoUpdate'), (on) => { setSwitch($('autoUpdate'), on); self.SimplApp.setAutoUpdate(on); });
    onSwitch($('loginItem'), async (on) => {
      setSwitch($('loginItem'), on);
      const r = await self.SimplApp.setLoginItem(on).catch(() => null);
      if (r && r.ok === false) { setSwitch($('loginItem'), !on); flash(r.message || 'Could not change the login item', true); }
    });
  }

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
        list.push({ id, code: c.course_code || c.name, name: c.name, originalName: c.original_name || c.name, nickname: c.original_name ? c.name : '', color: colors[`course_${id}`] || fallback[list.length % fallback.length], favorite: !!c.is_favorite || favIds.has(id) });
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
      // a nickname is Canvas's own (it shows everywhere, in Canvas too); an empty field removes it
      const nick = h('input', { class: 'course__nick', type: 'text', placeholder: 'Nickname', value: c.nickname || '', maxlength: '60', 'aria-label': `Nickname for ${c.originalName}` });
      nick.addEventListener('change', async () => {
        const v = nick.value.trim();
        if (v === (c.nickname || '')) return;
        try {
          await canvasWrite(v ? 'PUT' : 'DELETE', `/api/v1/users/self/course_nicknames/${c.id}${v ? `?nickname=${encodeURIComponent(v)}` : ''}`);
          c.nickname = v;
          c.name = v || c.originalName;
          flash();
        } catch (e) {
          nick.value = c.nickname || '';
          flash(`Not saved: ${e?.message || e}`, true);
        }
      });
      const row = h('div', { class: `course ${on ? '' : 'is-off'}`, dataset: { course: c.id }, style: { animationDelay: `${Math.min(i * 45, 300)}ms` } }, [
        h('span', { class: 'course__dot', style: { background: c.color } }),
        h('span', { class: 'course__body' }, [h('span', { class: 'course__code', text: c.code }), h('span', { class: 'course__name', text: c.originalName })]),
        nick, seg, sw,
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
    grades.goal = Number.isFinite(p.gpaGoal) ? p.gpaGoal : 4; // the same goal the setup starts from
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
    const since = [grades.tracking?.since, grades.snaps[0]?.date].filter(Boolean).sort()[0]; // the earliest day the history holds, imported ones included
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
  // Import CSV: a history exported earlier — from another browser, another device, or before a
  // reset — comes back in. The file is what Export CSV writes (date,term_gpa per line, the header
  // optional). A date already recorded here is kept as it is; the rest are added, and the history
  // is sorted by date. Tracking itself is left as it was.
  $('importCsv').addEventListener('click', () => $('importCsvFile').click());
  $('importCsvFile').addEventListener('change', async (e) => {
    const file = e.target.files?.[0];
    e.target.value = '';
    if (!file) return;
    let rows;
    try {
      rows = parseGpaCsv(await file.text());
    } catch {
      flash('That file is not a GPA export', true);
      return;
    }
    const have = new Set(grades.snaps.map((s) => s.date));
    const added = rows.filter((r) => !have.has(r.date));
    if (added.length) {
      grades.snaps = [...grades.snaps, ...added].sort((a, b) => (a.date < b.date ? -1 : a.date > b.date ? 1 : 0));
      try { await setPref('gpaSnapshots', grades.snaps); } catch { flash('Could not save the history', true); return; }
      paintGrades();
    }
    flash(added.length ? `Imported ${added.length} ${added.length === 1 ? 'day' : 'days'}` : 'Nothing new to import');
  });
  /** The rows of a GPA export: a date and a term GPA on the 4.0 scale; a row with no GPA carries
   *  nothing and is skipped; anything else is not a GPA export. */
  function parseGpaCsv(text) {
    const out = new Map();
    for (const raw of String(text).split(/\r?\n/)) {
      const line = raw.trim();
      if (!line) continue;
      const [d, g] = line.split(',').map((s) => (s || '').trim().replace(/^"|"$/g, ''));
      if (!/^\d{4}-\d{2}-\d{2}$/.test(d)) { if (/^date$/i.test(d)) continue; throw new Error('not a GPA export'); }
      if (g === '' || g === undefined) continue;
      const gpa = Number(g);
      if (!Number.isFinite(gpa) || gpa < 0 || gpa > 4) throw new Error('not a GPA export');
      out.set(d, { date: d, gpa });
    }
    if (!out.size) throw new Error('not a GPA export');
    return [...out.values()];
  }
  function download(name, text, type) {
    if (inApp) { self.SimplApp.saveFile(name, text).then((r) => { if (r && r.ok) flash('Saved'); else if (r && !r.cancelled) flash(r.message || 'Could not save', true); }); return; }
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([text], { type }));
    a.download = name;
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 1000);
  }
  if (site.host) loadGrades();

  // ---- text fields shared by the sections below ----------------------------------------------------
  const TEXT = [['siteName', 'appearance.siteName'], ['logoUrl', 'appearance.logoUrl']];
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
  // ---- Appearance ----------------------------------------------------------------------------------
  $('themes').addEventListener('click', (e) => {
    const b = e.target.closest('.theme');
    if (b) save({ appearance: { darkMode: b.dataset.value } });
  });
  onSeg($('sideCourses'), (v) => save({ appearance: { sideCourses: v } }));
  // the Dashboard's views: a switch may not take the last one off — a dashboard with nothing on it is not one
  const DASH = [['dashCards', 'cards'], ['dashList', 'list'], ['dashActivity', 'activity']];
  for (const [id, key] of DASH) {
    onSwitch($(id), (on) => {
      const d = { cards: true, list: true, activity: true, ...(settings.appearance.dashboard || {}) };
      if (!on && !DASH.some(([, k]) => k !== key && d[k] !== false)) { setSwitch($(id), true); return; }
      save({ appearance: { dashboard: { [key]: on } } });
    });
  }

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
    msg.textContent = r.message || `Enabled on ${origin}. Reload that tab.`;
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
  async function renderStats() {
    if (inApp) {
      const st = self.SimplApp.state() || {};
      const extState = { on: 'on', off: 'off', missing: 'not found' }[st.extension?.state] || '…';
      $('dataStats').replaceChildren(
        h('div', { class: 'stat' }, [h('span', { text: 'Settings kept' }), h('b', { text: 'in the app, shared with Safari' })]),
        h('div', { class: 'stat' }, [h('span', { text: 'Canvas data kept here' }), h('b', { text: 'none' })]),
        h('div', { class: 'stat' }, [h('span', { text: 'Extension in Safari' }), h('b', { text: extState })]),
        h('div', { class: 'stat' }, [h('span', { text: 'Grade history' }), h('b', { text: 'in Safari, on the Grades page' })]),
      );
      return;
    }
    let snaps = 0;
    let sites = 0;
    try {
      const all = await api.storage.local.get(null);
      for (const k of Object.keys(all)) {
        if (k.startsWith('prefs:')) { sites += 1; if (Array.isArray(all[k]?.gpaSnapshots)) snaps += all[k].gpaSnapshots.length; }
      }
    } catch {
      /* ignore */
    }
    $('dataStats').replaceChildren(
      h('div', { class: 'stat' }, [h('span', { text: 'Canvas data kept here' }), h('b', { text: 'none' })]),
      h('div', { class: 'stat' }, [h('span', { text: 'Grade snapshots' }), h('b', { text: snaps ? `${snaps} ${snaps === 1 ? 'day' : 'days'}` : (grades.tracking ? 'from today' : 'none') })]),
      h('div', { class: 'stat' }, [h('span', { text: 'Sites with preferences' }), h('b', { text: String(sites) })]),
    );
  }
  // ---- Uninstalling: the steps for the browser this page is open in --------------------------------------
  // On a Mac the extension lives inside the app, so deleting the app removes it; Chrome, Edge and
  // Firefox delete an extension's storage when it is removed; iOS deletes the app's data with the app.
  // Reset everything above is what clears the settings, keys and history in every case.
  function paintUninstall() {
    const proto = location.protocol;
    const platform = inApp || proto === 'safari-web-extension:' ? 'safari' : proto === 'file:' ? 'ios' : proto === 'moz-extension:' ? 'firefox' : 'chrome';
    const li = (parts) => h('li', {}, parts.map((p) => (typeof p === 'string' ? document.createTextNode(p) : p)));
    const b = (t) => h('b', { text: t });
    const code = (t) => h('code', { text: t });
    const reset = li(['Press ', b('Reset everything'), ' above. It clears your preferences, grade history and keys, gives up the sites you added, and clears the one-line note kept on every open Canvas tab.']);
    const steps = {
      safari: [
        reset,
        li(['Quit Safari, then drag ', b('Simpl Courses'), ' from the Applications folder to the Trash. Safari reads the extension out of the app, so the extension goes with it and leaves the Extensions list on the next launch.']),
      ],
      chrome: [
        reset,
        li(['Open ', code('chrome://extensions'), ' (Edge: ', code('edge://extensions'), '), find Simpl Courses and press ', b('Remove'), '. The browser deletes the extension’s storage with it.']),
      ],
      firefox: [
        reset,
        li(['Open ', code('about:addons'), ', find Simpl Courses and choose ', b('Remove'), '. Firefox deletes the extension’s storage with it.']),
      ],
      ios: [
        li(['Want your Canvas session cleared as well? Press ', b('Sign out'), ' in Settings first.']),
        li(['On the Home Screen touch and hold the Simpl Courses icon, then ', b('Remove App → Delete App'), '. iOS deletes the app’s data with it: settings, keys, grade history and the saved session.']),
      ],
    };
    $('uninstallSub').textContent = platform === 'ios' ? 'How to take Simpl Courses off this iPhone with everything it stored.' : `How to take Simpl Courses off this ${platform === 'safari' ? 'Mac' : 'browser'} with everything it stored.`;
    $('uninstallSteps').replaceChildren(...steps[platform]);
    $('uninstallNote').textContent = 'Nothing else is kept anywhere: your Canvas account, favourites and course nicknames live on Canvas and stay as they are.';
  }
  paintUninstall();
  $('exportSettings').addEventListener('click', () => {
    const copy = JSON.parse(JSON.stringify(settings));
    download('simpl-courses-settings.json', JSON.stringify(copy, null, 2), 'application/json');
  });
  $('importSettings').addEventListener('click', async () => {
    if (!inApp) { $('importFile').click(); return; }
    const r = await self.SimplApp.openFile(['json']).catch(() => null);
    if (!r || !r.ok) { if (r && !r.cancelled) flash(r.message || 'Could not open that file', true); return; }
    await importSettingsText(r.text);
  });
  $('importFile').addEventListener('change', async (e) => {
    const file = e.target.files?.[0];
    if (!file) return;
    await importSettingsText(await file.text());
    e.target.value = '';
  });
  async function importSettingsText(text) {
    try {
      const data = JSON.parse(text);
      if (!data || typeof data !== 'object' || Array.isArray(data)) throw new Error('shape');
      settings = await S.replace(data);
      flash('Imported');
      paintAll();
    } catch {
      flash('That file is not a settings export', true);
    }
  }
  $('resetSettings').addEventListener('click', async () => {
    if (!confirm('Reset everything? Preferences, grade history, the sites you added and your API keys are cleared, the extension gives up its access to those sites, and the note it keeps on open Canvas tabs is cleared.')) return;
    for (const origin of settings.domains || []) {
      await send({ type: 'unregisterDomain', origin });
      try { await api.permissions.remove({ origins: [`${origin}/*`] }); } catch { /* not granted */ }
    }
    try { await api.storage.local.clear(); } catch { /* ignore */ }
    settings = await S.replace({});
    await send({ type: 'wipeSiteNotes' }); // last: the copy each open Canvas tab keeps in its own site storage
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
    [...$('themes').querySelectorAll('.theme')].forEach((b) => b.classList.toggle('is-on', b.dataset.value === (settings.appearance.darkMode || 'system')));
    setSeg($('sideCourses'), settings.appearance.sideCourses || 'always');
    for (const [id, key] of DASH) setSwitch($(id), settings.appearance.dashboard?.[key] !== false);
    renderDomains();
    paintStatus();
    paint();
    listenScheme();
  }
  paintAll();
  S.onChange((s) => { settings = s; paintAll(); });

  // ---- Developer: catching a tool's own window ------------------------------------------------
  // Hidden until asked for — five presses on the version, or #dev — because it is for finding out
  // what a browser reports about a new tab, not for using Canvas. Everything it sets is one code.
  const DEV = BCV.devcode;
  const devAsk = (msg) => api.runtime.sendMessage(msg).catch(() => null);
  let devNow = { ...DEV.SHIPPED };
  function devPaintFields() {
    $('devFields').replaceChildren(...DEV.FIELDS.map((f) => {
      const opts = f.words.map((w, i) => {
        const b2 = document.createElement('button');
        b2.type = 'button';
        b2.className = `devopt${devNow[f.key] === i ? ' is-on' : ''}`;
        b2.textContent = w;
        b2.onclick = () => devApply({ ...devNow, [f.key]: i });
        return b2;
      });
      const row = document.createElement('div');
      row.className = 'devfield';
      row.innerHTML = `<span class="devfield__body"><span class="row__t row__t--med"></span></span>`;
      row.querySelector('.row__t').textContent = f.label;
      const wrap = document.createElement('span');
      wrap.className = 'devfield__opts';
      wrap.append(...opts);
      row.append(wrap);
      return row;
    }));
  }
  function devPaint() {
    $('devCode').value = DEV.encode(devNow);
    devPaintFields();
  }
  async function devApply(settingsNext) {
    const r = await devAsk({ type: 'devSet', settings: settingsNext });
    devNow = r?.settings || settingsNext;
    devPaint();
    $('devMsg').textContent = `Now ${DEV.encode(devNow)} — ${DEV.explain(devNow).join(' · ')}`;
  }
  function devRows(r) {
    if (!r?.rows?.length) return 'Nothing yet. Open a tool, press the button that wants a new window, then Refresh.';
    const t0 = r.rows[0].at;
    return r.rows.map((x) => {
      const secs = `${((x.at - t0) / 1000).toFixed(1)}s`.padStart(7);
      if (x.kind === 'popup') return `${secs}  popup ${x.open ? 'opened' : 'closed'} on tab ${x.tabId}`;
      if (x.kind === 'allow') return `${secs}  our own new tab coming from tab ${x.tabId}`;
      return `${secs}  new tab ${x.tabId} opener=${x.openerTabId ?? '—'} window=${x.windowId} url=${x.url || '—'} pending=${x.pendingUrl || '—'}\n         → ${x.action}`;
    }).join('\n');
  }
  async function devShow(clear = false) {
    const r = await devAsk({ type: 'devLog', clear });
    $('devLog').textContent = devRows(r);
    if (!r) { $('devSeenSub').textContent = 'The background is not answering.'; return; }
    const can = r.can || {};
    const says = [
      `tabs ${can.tabs ? 'yes' : 'NO'}`,
      `onCreated ${can.onCreated ? 'yes' : 'NO'}`,
      `heard one ${can.heardATab ? 'yes' : 'NOT YET'}`,
      `windows ${can.windows ? 'yes' : 'no'}`,
      `webNavigation ${can.webNavigation ? 'yes' : 'no'}`,
    ].join(' · ');
    $('devSeenSub').textContent = `Code ${r.code} · open on ${r.open?.length ? `tab ${r.open.join(', ')}` : 'no tab'} · this browser: ${says}`;
  }
  async function devInit() {
    const r = await devAsk({ type: 'devGet' });
    devNow = r?.settings || { ...DEV.SHIPPED };
    devPaint();
    devShow();
  }
  $('devUse').onclick = () => {
    const parsed = DEV.decode($('devCode').value);
    if (!parsed.ok) { $('devMsg').textContent = parsed.message; return; }
    devApply(parsed.settings);
  };
  $('devEverything').onclick = () => devApply({ ...DEV.EVERYTHING });
  $('devShipped').onclick = () => devApply({ ...DEV.SHIPPED });
  $('devRefresh').onclick = () => devShow();
  $('devClear').onclick = () => devShow(true);
  $('devCopy').onclick = async () => {
    const text = `${DEV.encode(devNow)}\n${DEV.explain(devNow).join('\n')}\n\n${$('devLog').textContent}`;
    try { await navigator.clipboard.writeText(text); $('devMsg').textContent = 'Copied — paste it where it is wanted.'; } catch { $('devMsg').textContent = 'Could not copy; select the log and copy it by hand.'; }
  };
  let devTaps = 0;
  let devTapAt = 0;
  function revealDev() {
    if ($('dev').hidden) {
      $('dev').hidden = false;
      NAV_KEYS.add('dev'); // (showSection only opens what the nav knows about)
      const tile = h('span', { class: 'navlink__tile' });
      tile.innerHTML = '<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 7l-5 5 5 5M15 7l5 5-5 5"/></svg>';
      nav.append(h('button', { type: 'button', class: 'navlink', dataset: { section: 'dev' }, onclick: () => { history.replaceState(null, '', '#dev'); showSection('dev'); } }, [tile, h('span', { class: 'navlink__label', text: 'Developer' }), h('span', { class: 'navlink__dot', id: 'dot-dev', hidden: true })]));
      devInit();
    }
    showSection('dev');
  }
  $('version').addEventListener('click', () => {
    const now = Date.now();
    devTaps = now - devTapAt > 1200 ? 1 : devTaps + 1;
    devTapAt = now;
    if (devTaps >= 5) { devTaps = 0; revealDev(); }
  });
  if (location.hash === '#dev') revealDev();

  showSection(location.hash === '#dev' ? 'dev' : (location.hash ? location.hash.slice(1) : NAV[0][0])); // (This Mac in the app's window, General everywhere else)
})();
