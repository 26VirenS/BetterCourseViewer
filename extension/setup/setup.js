/* The guided setup page: welcome → the Canvas address (that host alone gets permission) → the
 * courses, read from Canvas (unchecked ones stay hidden everywhere: they are the favourites every
 * screen follows) → grades (tracking, a goal, a target per course) → the smart panel, optional →
 * done, and "Open Canvas" starts the tour on arrival. Opened once after install (background.js),
 * and any time from the toolbar button or Settings. */
(async function () {
  const BCV = self.BCV;
  const api = BCV.api;
  const S = BCV.settings;
  const { h } = BCV.utils;
  const $ = (id) => document.getElementById(id);

  // Promise-only messaging (Safari rejects callback arguments on these APIs).
  const send = (msg) => new Promise((resolve) => {
    try {
      const p = api.runtime.sendMessage(msg);
      if (p && typeof p.then === 'function') p.then(resolve, () => resolve(null));
      else resolve(p ?? null);
    } catch {
      resolve(null);
    }
  });

  // ---- appearance: the extension's choice, else the system's ------------------------------------
  const mq = window.matchMedia?.('(prefers-color-scheme: dark)');
  let settings = await S.get();
  const paint = () => {
    const mode = settings.appearance?.darkMode || 'system';
    const dark = mode === 'on' || (mode === 'system' && !!mq?.matches);
    document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
  };
  paint();
  mq?.addEventListener?.('change', paint);
  S.onChange((s) => { settings = s; paint(); });

  // ---- state --------------------------------------------------------------------------------------
  const CHECK = 'M20 6L9 17l-5-5';
  const CROSS = 'M6 6l12 12M18 6L6 18';
  const GRADES = [['A', 0], ['A-', 1], ['B+', 2], ['B', 3], ['C', 6]]; // letter → index on the Grades page's scale
  const PROVIDERS = [
    { key: 'claude', name: 'Claude', note: 'Available now', ready: true, settingsKey: 'claudeKey', placeholder: 'sk-ant-…', url: 'https://console.anthropic.com/settings/keys', host: 'console.anthropic.com', steps: ['API keys → Create key', 'Paste it above. It is shown only once.'] },
    { key: 'openai', name: 'ChatGPT', note: 'Available now', ready: true, settingsKey: 'openaiKey', placeholder: 'sk-…', url: 'https://platform.openai.com/api-keys', host: 'platform.openai.com', steps: ['API keys → Create new secret key', 'Paste it above. It is shown only once.'] },
    { key: 'gemini', name: 'Gemini', note: 'Coming soon', ready: false },
  ];
  const LABELS = ['Get started', 'Continue', 'Continue', 'Continue', 'Finish', 'Open Canvas'];
  const st = {
    step: 0, dir: 1,
    hostInput: '', host: '', origin: '', verified: false, me: null, hostError: null,
    scanning: false, scanError: null, courses: [], favs: new Set(),
    tracking: true, goal: 3.5, targets: {},
    provider: settings.smart?.openaiKey && !settings.smart?.claudeKey ? 'openai' : 'claude', key: '', keyOk: false, keyMsg: '',
    skipped: false,
  };

  const svg = (d, { size = 14, stroke = 'currentColor', width = 2.2, cls = '' } = {}) => {
    const el = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    el.setAttribute('viewBox', '0 0 24 24');
    el.setAttribute('width', size);
    el.setAttribute('height', size);
    el.setAttribute('fill', 'none');
    el.setAttribute('stroke', stroke);
    el.setAttribute('stroke-width', width);
    el.setAttribute('stroke-linecap', 'round');
    el.setAttribute('stroke-linejoin', 'round');
    el.setAttribute('aria-hidden', 'true');
    if (cls) el.setAttribute('class', cls);
    const p = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    p.setAttribute('d', d);
    el.append(p);
    return el;
  };
  const stagger = (nodes, step = 50, cap = 340) => nodes.forEach((n, i) => { n.style.animationDelay = `${Math.min(i * step, cap)}ms`; });
  const gpa2 = (n) => n.toFixed(2);

  // ---- the Canvas host --------------------------------------------------------------------------
  /** "school.instructure.com", "https://canvas.school.edu/login", "localhost:8787" → { host, origin } or null. */
  function parseHost(raw) {
    let s = String(raw || '').trim().toLowerCase();
    if (!s) return null;
    if (!/^https?:\/\//.test(s)) s = `https://${s}`;
    let url;
    try { url = new URL(s); } catch { return null; }
    const host = url.host;
    if (!host || !(host.includes('.') || /^localhost(:\d+)?$/.test(host)) || host.endsWith('.')) return null;
    const scheme = /^localhost(:\d+)?$|^127\.0\.0\.1(:\d+)?$/.test(host) && /^http:\/\//.test(String(raw).trim().toLowerCase()) ? 'http' : url.protocol.replace(':', '');
    return { host, origin: `${scheme}://${host}` };
  }
  const builtIn = (host) => /\.instructure\.com$/i.test(host.replace(/:\d+$/, ''));
  const canvasGet = (path) => fetch(`${st.origin}${path}`, { credentials: 'include', headers: { Accept: 'application/json' } }).then(async (r) => {
    const text = await r.text();
    if (!r.ok) { const e = new Error(`HTTP ${r.status}`); e.status = r.status; throw e; }
    return JSON.parse(text.replace(/^while\(1\);/, ''));
  });

  /** Permission for the host (from the click), its scripts registered, and a Canvas answer. */
  async function enableHost() {
    const parsed = parseHost(st.hostInput);
    if (!parsed) return false;
    st.host = parsed.host;
    st.origin = parsed.origin;
    st.hostError = null;
    if (!builtIn(st.host)) {
      let granted = false;
      try {
        granted = await api.permissions.contains({ origins: [`${st.origin}/*`] });
      } catch { granted = false; }
      if (!granted) {
        try {
          granted = await api.permissions.request({ origins: [`${st.origin}/*`] });
        } catch (e) {
          st.hostError = { kind: 'permission', text: `The browser did not grant the site: ${e?.message || e}` };
          return false;
        }
      }
      if (!granted) {
        st.hostError = { kind: 'permission', text: 'Permission was not granted. Simpl Courses can only run on a site you allow.' };
        return false;
      }
      const r = await send({ type: 'registerDomain', origin: st.origin });
      if (r && r.ok === false) {
        st.hostError = { kind: 'permission', text: r.message || 'The site could not be enabled.' };
        return false;
      }
    }
    try {
      st.me = await canvasGet('/api/v1/users/self');
      st.verified = true;
      return true;
    } catch (e) {
      st.verified = false;
      if (e.status === 401 || e.status === 403) st.hostError = { kind: 'signin', text: 'Canvas answered, but you are not signed in there yet.' };
      else st.hostError = { kind: 'notcanvas', text: e.status ? `That address answered ${e.status}, not like Canvas.` : 'That address could not be reached. Check it, and that you are online.' };
      return false;
    }
  }

  // ---- the courses ---------------------------------------------------------------------------------
  async function scan() {
    st.scanning = true;
    st.scanError = null;
    try {
      const [favs, all, colors] = await Promise.all([
        canvasGet('/api/v1/users/self/favorites/courses?per_page=100').catch(() => []),
        canvasGet('/api/v1/courses?enrollment_state=active&include[]=term&include[]=favorites&per_page=100'),
        canvasGet('/api/v1/users/self/colors').then((r) => r?.custom_colors || {}).catch(() => ({})),
      ]);
      const now = Date.now();
      const fallback = ['#34c759', '#30b0c7', '#ff2d55', '#c8901c', '#ff9500', '#5856d6', '#af52de', '#0a84ff'];
      const seen = new Set();
      const list = [];
      for (const c of Array.isArray(all) ? all : []) {
        const id = String(c.id);
        if (seen.has(id) || c.access_restricted_by_date) continue;
        if (c.workflow_state && c.workflow_state !== 'available') continue;
        const end = c.term?.end_at || c.end_at;
        if (end && new Date(end).getTime() < now) continue; // concluded courses never appear
        seen.add(id);
        list.push({ id, code: c.course_code || c.name, name: c.name, color: colors[`course_${id}`] || fallback[list.length % fallback.length], favorite: !!c.is_favorite });
      }
      const favIds = new Set((Array.isArray(favs) ? favs : []).map((c) => String(c.id)));
      for (const c of list) if (favIds.has(c.id)) c.favorite = true;
      st.courses = list;
      const starred = list.filter((c) => c.favorite).map((c) => c.id);
      st.favs = new Set(starred.length ? starred : list.map((c) => c.id)); // until something is starred, Canvas shows every course
      for (const c of list) if (!(c.id in st.targets)) st.targets[c.id] = 'A';
    } catch (e) {
      st.scanError = e?.message || 'The course list could not be read.';
      st.courses = [];
    }
    st.scanning = false;
  }

  // ---- the shell: progress, footer, transitions --------------------------------------------------
  const card = $('card');
  const body = $('body');
  const foot = $('foot');
  const progress = $('progress');
  const stepLabel = $('stepLabel');
  const skipBtn = $('skip');
  const reduced = () => !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;

  function paintChrome() {
    const labels = ['', '1 of 4', '2 of 4', '3 of 4', '4 of 4', 'Done'];
    stepLabel.textContent = labels[st.step];
    [...progress.children].forEach((seg, i) => {
      seg.classList.toggle('is-done', i < st.step);
      seg.classList.toggle('is-current', i === st.step);
    });
    skipBtn.hidden = st.step === 5;
  }

  /** Replace the card's content with the next step: the old one slides out, the card's height
   *  eases to the new size, the new one slides in. */
  async function transitionTo(build, dir = 1) {
    const h0 = card.getBoundingClientRect().height;
    if (!reduced() && body.childElementCount) {
      body.style.setProperty('--leave', `${-16 * dir}px`);
      body.classList.add('is-leaving');
      await new Promise((r) => setTimeout(r, 170));
    }
    body.classList.remove('is-leaving', 'is-entered');
    body.replaceChildren();
    foot.replaceChildren();
    build();
    paintChrome();
    if (reduced()) return;
    body.style.setProperty('--enter', `${16 * dir}px`);
    body.classList.add('is-entering');
    card.style.height = `${h0}px`;
    const h1 = (() => { card.style.height = 'auto'; const v = card.getBoundingClientRect().height; card.style.height = `${h0}px`; return v; })();
    void card.offsetHeight; // commit the start height
    card.style.height = `${h1}px`;
    requestAnimationFrame(() => requestAnimationFrame(() => {
      body.classList.remove('is-entering');
      body.classList.add('is-entered');
    }));
    const done = (e) => { if (e.target === card && e.propertyName === 'height') { card.style.height = 'auto'; card.removeEventListener('transitionend', done); } };
    card.addEventListener('transitionend', done);
    setTimeout(() => { card.style.height = 'auto'; }, 520); // belt and braces
  }
  /** The card's height follows content that changed in place (a switch, a list landing). */
  function settleHeight() {
    if (reduced()) return;
    const h0 = card.getBoundingClientRect().height;
    card.style.height = 'auto';
    const h1 = card.getBoundingClientRect().height;
    if (Math.abs(h1 - h0) < 1) return;
    card.style.height = `${h0}px`;
    void card.offsetHeight;
    card.style.height = `${h1}px`;
    setTimeout(() => { card.style.height = 'auto'; }, 460);
  }
  function shake() {
    card.classList.remove('is-shaking');
    void card.offsetWidth;
    card.classList.add('is-shaking');
  }

  function footer({ next = LABELS[st.step], onNext, disabled = false, back = st.step > 0 && st.step < 5, notNow = null } = {}) {
    const nextBtn = h('button', { type: 'button', class: 'btn', id: 'next', text: next, disabled: disabled || null });
    nextBtn.addEventListener('click', async () => {
      if (nextBtn.disabled) return;
      nextBtn.classList.add('is-busy');
      nextBtn.disabled = true;
      try {
        await onNext();
      } catch (e) {
        console.error('[Simpl Courses setup]', e);
      }
      nextBtn.classList.remove('is-busy');
      nextBtn.disabled = disabled;
    });
    foot.append(...[
      back ? h('button', { type: 'button', class: 'btn btn--ghost', id: 'back', text: 'Back', onclick: () => go(st.step - 1, -1) }) : null,
      h('span', { class: 'foot__spacer' }),
      notNow ? h('button', { type: 'button', class: 'btn btn--quiet', id: 'notNow', text: notNow.label, onclick: notNow.onSelect }) : null,
      nextBtn,
    ].filter(Boolean)); // append() would print a null as text
    return nextBtn;
  }

  const STEPS = [welcome, address, courses, grades, smart, done]; // function declarations below

  function go(n, dir = n > st.step ? 1 : -1) {
    st.step = Math.max(0, Math.min(5, n));
    transitionTo(STEPS[st.step], dir);
  }

  // ---- steps ---------------------------------------------------------------------------------------
  function welcome() {
    const mark = h('span', { class: 'mark mark--lg', 'aria-hidden': 'true' });
    mark.innerHTML = `<svg viewBox="0 0 120 120" width="76" height="76"><defs><linearGradient id="sheetLg" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#fff" stop-opacity=".9"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient></defs><rect class="sheet" x="16" y="18" width="53" height="84" rx="14" fill="url(#sheetLg)"/><path class="arc arc--1" style="--len:178" d="M28 30 H69 A30 30 0 0 1 69 90 H28" fill="none" stroke="#fff" stroke-width="8" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="178"/><path class="arc arc--2" style="--len:126" d="M35 42 H69 A18 18 0 0 1 69 78 H35" fill="none" stroke="rgba(255,255,255,.72)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="126"/><path class="arc arc--3" style="--len:74" d="M42 54 H69 A6 6 0 0 1 69 66 H42" fill="none" stroke="rgba(255,255,255,.46)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="74"/></svg>`;
    body.append(h('div', { class: 'welcome' }, [
      mark,
      h('h1', { class: 'h1', text: 'Simpl Courses' }),
      h('p', { class: 'lead', text: 'A calmer layer over Canvas. Four quick questions.' }),
    ]));
    footer({ onNext: () => go(1), back: false });
  }

  function address() {
    const tick = svg(CHECK, { size: 16, stroke: '#34c759', width: 2.6, cls: 'tick' });
    const input = h('input', { type: 'text', id: 'host', placeholder: 'school.instructure.com', autocomplete: 'off', spellcheck: 'false', autocapitalize: 'off', value: st.hostInput });
    const field = h('div', { class: 'field' }, [input, tick]);
    const notice = h('div', { class: 'notice', id: 'hostNotice', hidden: true });
    let nextBtn = null;
    const lines = h('div', { class: 'lines' }, [
      ['Reads Canvas pages on this domain', CHECK, '#34c759', ''],
      ['Settings stay in this browser', CHECK, '#34c759', ''],
      ['No other sites, no servers', CROSS, 'var(--ink3)', 'line--off'],
    ].map(([label, icon, color, cls]) => h('div', { class: `line ${cls}` }, [svg(icon, { size: 14, stroke: color, width: 2.2 }), h('span', { text: label })])));
    stagger([...lines.children], 60);
    body.append(
      h('h1', { class: 'h1', text: 'Your Canvas address' }),
      h('p', { class: 'sub', text: 'This is the only site the extension runs on.' }),
      field, notice, lines,
    );
    const sync = () => {
      st.hostInput = input.value;
      const ok = !!parseHost(st.hostInput);
      field.classList.toggle('is-ok', ok);
      field.classList.remove('is-bad');
      tick.hidden = !ok;
      nextBtn.disabled = !ok;
      if (notice.hidden === false) { notice.hidden = true; settleHeight(); }
    };
    const showNotice = () => {
      const e = st.hostError;
      notice.replaceChildren(
        h('div', { class: 'notice__t', text: e.kind === 'signin' ? 'Sign in to Canvas first' : e.kind === 'permission' ? 'The site was not enabled' : 'Not a Canvas address?' }),
        h('div', { text: e.text }),
        h('div', { class: 'notice__row' }, [
          e.kind === 'signin' ? h('button', { type: 'button', class: 'btn btn--sm', text: 'Open Canvas to sign in', onclick: () => api.tabs.create({ url: `${st.origin}/login` }) }) : null,
          h('button', { type: 'button', class: 'btn btn--sm btn--ghost', text: 'Try again', onclick: () => nextBtn.click() }),
        ]),
      );
      notice.hidden = false;
      field.classList.add('is-bad');
      settleHeight();
      shake();
    };
    nextBtn = footer({
      disabled: !parseHost(st.hostInput),
      onNext: async () => {
        if (await enableHost()) {
          st.courses = [];
          go(2);
        } else showNotice();
      },
    });
    input.addEventListener('input', sync);
    input.addEventListener('keydown', (e) => { if (e.key === 'Enter' && !nextBtn.disabled) nextBtn.click(); });
    sync();
    setTimeout(() => input.focus({ preventScroll: true }), 60);
  }

  function courses() {
    const head = h('h1', { class: 'h1', text: 'Finding your courses' });
    const sub = h('p', { class: 'sub', text: 'Reading your enrollments.' });
    const wrap = h('div');
    body.append(head, sub, wrap);
    const nextBtn = footer({ disabled: true, onNext: () => go(3) });

    const ghostList = () => {
      const ghosts = h('div', { class: 'ghosts' }, [0, 1, 2, 3, 4].map(() => h('span', { class: 'ghost' })));
      [...ghosts.children].forEach((g, i) => { g.style.animationDelay = `${i * 130}ms`; });
      const spinner = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
      spinner.setAttribute('viewBox', '0 0 50 50'); spinner.setAttribute('width', '15'); spinner.setAttribute('height', '15'); spinner.setAttribute('class', 'spinner');
      spinner.innerHTML = '<circle cx="25" cy="25" r="20" fill="none" stroke="var(--fill)" stroke-width="6"></circle><circle cx="25" cy="25" r="20" fill="none" stroke="#0a84ff" stroke-width="6" stroke-linecap="round" stroke-dasharray="32 100"></circle>';
      return [ghosts, h('div', { class: 'scanning' }, [spinner, h('span', { text: 'Reading enrollments' })])];
    };
    const draw = () => {
      const list = st.courses;
      const count = h('span', { text: `${st.favs.size} selected` });
      const allBtn = h('button', { type: 'button', class: 'linkbtn', id: 'selectAll', text: st.favs.size === list.length ? 'Clear' : 'All', onclick: () => {
        const all = st.favs.size === list.length;
        st.favs = new Set(all ? [] : list.map((c) => c.id));
        draw();
      } });
      const rows = h('div', { class: 'rows' }, list.map((c) => {
        const on = st.favs.has(c.id);
        return h('button', { type: 'button', class: `row ${on ? 'is-on' : ''}`, dataset: { course: c.id }, onclick: (e) => {
          const el = e.currentTarget;
          if (st.favs.has(c.id)) st.favs.delete(c.id); else st.favs.add(c.id);
          el.classList.toggle('is-on', st.favs.has(c.id));
          count.textContent = `${st.favs.size} selected`;
          allBtn.textContent = st.favs.size === list.length ? 'Clear' : 'All';
          nextBtn.disabled = st.favs.size === 0;
        } }, [
          h('span', { class: 'row__dot', style: { background: c.color } }),
          h('span', { class: 'row__body' }, [h('span', { class: 'row__code', text: c.code }), h('span', { class: 'row__name', text: c.name })]),
          h('span', { class: 'row__box' }, svg(CHECK, { size: 12, stroke: '#fff', width: 3 })),
        ]);
      }));
      stagger([...rows.children], 50);
      head.textContent = 'Which are you in?';
      sub.textContent = 'Unchecked courses stay hidden everywhere.';
      wrap.replaceChildren(h('div', { class: 'listhead' }, [count, allBtn]), rows);
      nextBtn.disabled = st.favs.size === 0;
      settleHeight();
    };
    const drawEmpty = () => {
      head.textContent = st.scanError ? 'The courses could not be read' : 'No active courses';
      sub.textContent = st.scanError ? st.scanError : 'Between terms? Canvas lists nothing active right now.';
      wrap.replaceChildren(h('div', { class: 'empty' }, [
        h('span', { text: st.scanError ? 'Check that you are signed in to Canvas in this browser, then try again.' : 'You can finish setup now and choose courses later on the Courses page.' }),
        h('button', { type: 'button', class: 'btn btn--sm', text: 'Try again', onclick: run }),
      ]));
      nextBtn.disabled = false;
      settleHeight();
    };
    async function run() {
      head.textContent = 'Finding your courses';
      sub.textContent = 'Reading your enrollments.';
      wrap.replaceChildren(...ghostList());
      settleHeight();
      const t0 = Date.now();
      await scan();
      await new Promise((r) => setTimeout(r, Math.max(0, 450 - (Date.now() - t0)))); // the skeleton never flashes
      if (st.step !== 2) return;
      if (st.courses.length) draw(); else drawEmpty();
    }
    if (st.courses.length) draw(); else run();
  }

  function grades() {
    const chosen = st.courses.filter((c) => st.favs.has(c.id));
    const trackSwitch = h('button', { type: 'button', class: `switch ${st.tracking ? 'is-on' : ''}`, id: 'track', role: 'switch', 'aria-checked': st.tracking ? 'true' : 'false', 'aria-label': 'Track over time' }, h('span', { class: 'switch__knob' }));
    const goalVal = h('span', { class: 'stepper__val', id: 'goal', text: gpa2(st.goal) });
    const tick = () => { goalVal.classList.remove('is-tick'); void goalVal.offsetWidth; goalVal.classList.add('is-tick'); };
    const goalPanel = h('div', { class: 'panel', id: 'goalPanel' }, [
      h('span', { class: 'panel__label', text: 'GPA goal' }),
      h('div', { class: 'stepper' }, [
        h('button', { type: 'button', text: '−', 'aria-label': 'Lower the goal', onclick: () => { st.goal = Math.max(0, Math.round((st.goal - 0.05) * 100) / 100); goalVal.textContent = gpa2(st.goal); tick(); } }),
        goalVal,
        h('button', { type: 'button', text: '+', 'aria-label': 'Raise the goal', onclick: () => { st.goal = Math.min(4, Math.round((st.goal + 0.05) * 100) / 100); goalVal.textContent = gpa2(st.goal); tick(); } }),
      ]),
    ]);
    const reveal = h('div', { class: `reveal ${st.tracking ? '' : 'is-closed'}` }, goalPanel);
    trackSwitch.addEventListener('click', () => {
      st.tracking = !st.tracking;
      trackSwitch.classList.toggle('is-on', st.tracking);
      trackSwitch.setAttribute('aria-checked', st.tracking ? 'true' : 'false');
      reveal.style.height = `${goalPanel.getBoundingClientRect().height + 8}px`;
      void reveal.offsetHeight;
      reveal.classList.toggle('is-closed', !st.tracking);
      if (st.tracking) setTimeout(() => { reveal.style.height = ''; }, 400);
      settleHeight();
    });
    const targets = chosen.map((c) => h('div', { class: 'target', dataset: { course: c.id } }, [
      h('span', { class: 'row__dot', style: { background: c.color } }),
      h('span', { class: 'target__code', text: c.code }),
      h('div', { class: 'seg' }, GRADES.map(([letter]) => h('button', { type: 'button', class: `seg__b ${(st.targets[c.id] || 'A') === letter ? 'is-on' : ''}`, text: letter, onclick: (e) => {
        st.targets[c.id] = letter;
        [...e.currentTarget.parentNode.children].forEach((b) => b.classList.toggle('is-on', b === e.currentTarget));
      } }))),
    ]));
    stagger(targets, 50);
    body.append(
      h('h1', { class: 'h1', text: 'Grades' }),
      h('p', { class: 'sub', text: 'Canvas keeps no history. Simpl Courses can, locally.' }),
      h('div', { class: 'panel' }, [h('span', { class: 'panel__label', text: 'Track over time' }), trackSwitch]),
      reveal,
      h('div', { class: 'kicker', text: 'Target grade' }),
      h('div', { class: 'targets' }, targets.length ? targets : [h('div', { class: 'empty', text: 'No courses chosen: nothing to aim at yet.' })]),
    );
    footer({ onNext: () => go(4) });
  }

  function smart() {
    const provider = () => PROVIDERS.find((p) => p.key === st.provider) || PROVIDERS[0];
    const input = h('input', { type: 'password', id: 'key', placeholder: provider().placeholder, autocomplete: 'off', spellcheck: 'false', 'aria-label': 'API key', value: st.key });
    const tick = svg(CHECK, { size: 16, stroke: '#34c759', width: 2.6, cls: 'tick' });
    tick.hidden = !st.keyOk;
    const field = h('div', { class: 'field field--key' }, [input, tick]);
    const result = h('div', { class: 'result', id: 'keyResult', text: st.keyMsg });
    const steps = h('div', { class: 'keysteps' });
    const drawSteps = () => {
      const p = provider();
      input.placeholder = p.placeholder;
      steps.replaceChildren(
        h('div', { class: 'keystep' }, [h('b', { text: '1' }), h('span', {}, ['Sign in at ', h('a', { href: p.url, target: '_blank', rel: 'noopener', text: p.host })])]),
        ...p.steps.map((s2, i) => h('div', { class: 'keystep' }, [h('b', { text: String(i + 2) }), h('span', { text: s2 })])),
      );
      stagger([...steps.children], 60);
      settleHeight();
    };
    const provs = h('div', { class: 'provs' }, PROVIDERS.map((p) => h('button', { type: 'button', class: `prov ${p.ready ? '' : 'is-soon'} ${st.provider === p.key && p.ready ? 'is-on' : ''}`, dataset: { provider: p.key }, 'aria-disabled': p.ready ? null : 'true', onclick: () => {
      if (!p.ready) return;
      st.provider = p.key;
      st.keyOk = false;
      st.key = settings.smart?.[p.settingsKey] || '';
      input.value = st.key;
      tick.hidden = true;
      result.textContent = '';
      result.className = 'result';
      [...provs.children].forEach((b) => b.classList.toggle('is-on', b.dataset.provider === p.key));
      drawSteps();
    } }, [h('span', { class: 'prov__n', text: p.name }), h('span', { class: 'prov__s', text: p.note })])));
    stagger([...provs.children], 60);
    drawSteps();
    if (!st.key) st.key = settings.smart?.[provider().settingsKey] || '';
    input.value = st.key;
    body.append(
      h('h1', { class: 'h1', text: 'Smart panel' }),
      h('p', { class: 'sub', text: 'Optional. Runs on your own key, billed by the provider.' }),
      provs, field, result, steps,
    );
    const notNow = h('button', { type: 'button', class: 'btn btn--quiet', id: 'notNow', text: 'Not now', onclick: () => { st.key = ''; st.keyOk = false; go(5); } });
    const nextBtn = footer({
      onNext: async () => {
        const key = input.value.trim();
        if (!key) { go(5); return; }
        const p = provider();
        result.textContent = 'Checking the key…';
        result.className = 'result';
        // one cheap call before saving, so a typo surfaces here rather than mid-semester
        const r = await send({ type: 'testKey', provider: p.key, key });
        if (r?.ok) {
          st.key = key;
          st.keyOk = true;
          st.keyMsg = r.message || 'The key works.';
          await S.update({ smart: { [p.settingsKey]: key, preferred: p.key } });
          tick.hidden = false;
          result.textContent = st.keyMsg;
          result.className = 'result is-ok';
          await new Promise((res) => setTimeout(res, 350));
          go(5);
        } else {
          st.keyOk = false;
          result.textContent = r?.message || 'The key could not be checked. Check it and try again, or skip for now.';
          result.className = 'result is-err';
          field.classList.add('is-bad');
          shake();
          settleHeight();
        }
      },
    });
    nextBtn.before(notNow);
    input.addEventListener('input', () => {
      st.key = input.value;
      st.keyOk = false;
      tick.hidden = true;
      field.classList.remove('is-bad');
      notNow.hidden = input.value.trim().length > 0;
    });
    notNow.hidden = input.value.trim().length > 0;
  }

  function done() {
    const chosen = st.courses.filter((c) => st.favs.has(c.id));
    const check = h('span', { class: 'done__check', 'aria-hidden': 'true' });
    check.innerHTML = '<svg viewBox="0 0 24 24" width="31" height="31" fill="none" stroke="#34c759" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path class="arc" style="--len:30" d="M20 6L9 17l-5-5" stroke-dasharray="30"/></svg>';
    const providerName = st.keyOk ? (PROVIDERS.find((p) => p.key === st.provider)?.name || 'On') : 'Off';
    const rows = [
      ['Canvas', st.host || 'Not set', st.host ? '' : 'is-off'],
      ['Courses', st.verified ? `${chosen.length} selected` : '—', ''],
      ['Tracking', st.tracking ? (st.goal > 0 ? `Goal ${gpa2(st.goal)}` : 'On') : 'Off', st.tracking ? 'is-on' : 'is-off'],
      ['Smart panel', providerName, st.keyOk ? 'is-on' : 'is-off'],
    ].map(([k, v, cls]) => h('div', { class: 'summary__row' }, [h('span', { class: 'summary__k', text: k }), h('span', { class: `summary__v ${cls}`, text: v })]));
    stagger(rows, 70);
    body.append(
      h('div', { class: 'done' }, [check, h('h1', { class: 'h1', text: 'All set' }), h('p', { class: 'sub', text: st.host ? 'Open Canvas and it takes over.' : 'Open your Canvas site and Simpl Courses takes over.' })]),
      h('div', { class: 'summary' }, rows),
    );
    footer({
      next: st.host ? 'Open Canvas' : 'Close',
      back: false,
      onNext: async () => {
        if (!st.host) { window.close(); return; }
        if (!st.verified) { go(1, -1); return; } // the site was never granted: back to the address
        await save();
        try {
          await api.tabs.create({ url: `${st.origin}/` });
        } catch {
          location.href = `${st.origin}/`;
        }
        setTimeout(() => window.close(), 400);
      },
    });
  }

  /** Everything the steps decided, where the screens read it: the grade preferences under the
   *  host, and a one-shot plan (favourites to write through Canvas, the tour) that the interface
   *  applies on the first Canvas page and clears before the tour runs. Skipping writes no tour. */
  async function save() {
    if (!st.host) return;
    const prefKey = `prefs:${st.host}`;
    const all = await api.storage.local.get(prefKey);
    const prefs = (all && all[prefKey]) || {};
    prefs.setupDone = true;
    if (!st.skipped) {
      prefs.gpaGoal = st.goal;
      prefs.gpaTracking = st.tracking ? { priorGpa: null, priorCourses: 0, since: new Date().toISOString().slice(0, 10) } : null;
      const targets = { ...(prefs.gradeTargets || {}) };
      for (const c of st.courses) if (st.favs.has(c.id)) targets[c.id] = (GRADES.find(([l]) => l === (st.targets[c.id] || 'A')) || GRADES[0])[1];
      prefs.gradeTargets = targets;
    }
    await api.storage.local.set({
      [prefKey]: prefs,
      'setup:offered': true,
      'setup:plan': { host: st.host, favorites: st.skipped ? null : [...st.favs], tour: !st.skipped, at: Date.now() },
    });
  }

  skipBtn.addEventListener('click', async () => {
    st.skipped = true;
    await api.storage.local.set({ 'setup:offered': true });
    if (st.verified) go(5);
    else window.close();
  });

  await api.storage.local.set({ 'setup:offered': true }).catch(() => {});
  go(0, 1);
})();
