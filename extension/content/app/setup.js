/* The guided setup, drawn over the Canvas page the student is on (the "Simpl Courses Setup"
 * mockup's glass card, in a shadow root so Canvas's styles never reach it). Three steps:
 *   1 the courses, read from the enrolments (unchecked ones stay hidden everywhere: they become the
 *     Canvas favourites, the one list every screen follows) ·
 *   2 grades (tracking, a goal, a target letter per course) ·
 *   3 the smart panel, optional, with the steps to get a key ·
 * then straight into the tour. Opened by ?bcv=setup (the toolbar popup's Set up button, the
 * account sheet on a phone, the app's first launch). Skip writes the "done" flags and no tour. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const S = BCV.settings;
  const store = BCV.store;
  const html = document.documentElement;

  const CHECK = 'M20 6L9 17l-5-5';
  const GRADES = ['C', 'B', 'B+', 'A-', 'A', 'A+']; // the target letters, lowest on the left (saved as the letter the Grades page reads)
  const PROVIDERS = [
    { key: 'claude', name: 'Claude', note: 'Available now', ready: true, settingsKey: 'claudeKey', placeholder: 'sk-ant-…', url: 'https://console.anthropic.com/settings/keys', host: 'console.anthropic.com', steps: ['API keys → Create key', 'Paste it above. It is shown only once.'] },
    { key: 'openai', name: 'ChatGPT', note: 'Available now', ready: true, settingsKey: 'openaiKey', placeholder: 'sk-…', url: 'https://platform.openai.com/api-keys', host: 'platform.openai.com', steps: ['API keys → Create new secret key', 'Paste it above. It is shown only once.'] },
    { key: 'gemini', name: 'Gemini', note: 'Coming soon', ready: false },
  ];
  const LABELS = ['Continue', 'Continue', 'Finish'];
  const STEP_LABELS = ['1 of 3', '2 of 3', '3 of 3', 'Done'];
  const PURPOSE = 'Smart Panel is intended to be a smart assistant that helps with learning. It is not intended to help complete assignments, cheat on quizzes, or any other purpose than to assist with learning.';
  const MARK = '<svg viewBox="0 0 120 120" width="23" height="23" aria-hidden="true"><defs><linearGradient id="sheetSm" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#fff" stop-opacity=".9"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient></defs><rect x="16" y="18" width="53" height="84" rx="14" fill="url(#sheetSm)"/><path d="M28 30 H69 A30 30 0 0 1 69 90 H28" fill="none" stroke="#fff" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/><path d="M35 42 H69 A18 18 0 0 1 69 78 H35" fill="none" stroke="rgba(255,255,255,.72)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/><path d="M42 54 H69 A6 6 0 0 1 69 66 H42" fill="none" stroke="rgba(255,255,255,.46)" stroke-width="8" stroke-linecap="round" stroke-linejoin="round"/></svg>';

  let ui = null; // the open overlay: { host, overlay, card, body, foot, progress, stepLabel, skipBtn }
  let st = null;
  const active = () => !!ui;
  const reduced = () => !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
  const stagger = (nodes, step = 50, cap = 340) => nodes.forEach((n, i) => { n.style.animationDelay = `${Math.min(i * step, cap)}ms`; });
  const gpa2 = (n) => n.toFixed(2);
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

  // ---- open / close --------------------------------------------------------------------------------
  async function open(app) {
    if (ui) return;
    // The ?bcv=setup that opened this is dropped from the address at once: a reload lands on the
    // page itself, and the tour can start right here when the steps are done.
    const url = new URL(location.href);
    if (url.searchParams.get('bcv') === 'setup') {
      url.searchParams.delete('bcv');
      url.searchParams.delete('step');
      history.replaceState({ bcv: true }, '', url.pathname + url.search + url.hash);
      app.state.route = app.parseRoute();
    }
    const settings = await S.get();
    st = {
      app, settings, step: 0,
      scanning: false, scanError: null, courses: [], favs: new Set(), nicks: {},
      tracking: true, goal: 3.5, targets: {},
      provider: settings.smart?.openaiKey && !settings.smart?.claudeKey ? 'openai' : 'claude', key: '', keyOk: false, keyMsg: '',
      closing: false,
    };
    const host = h('div', { id: 'bcv-setup' });
    host.setAttribute('data-theme', app.isDark() ? 'dark' : 'light');
    const shadow = host.attachShadow({ mode: 'open' });
    const stepLabel = h('span', { class: 'top__step', id: 'stepLabel' });
    const skipBtn = h('button', { class: 'top__skip', id: 'skip', type: 'button', text: 'Skip', onclick: () => finish({ skipped: true }) });
    const progress = h('div', { class: 'progress', id: 'progress', 'aria-hidden': 'true' }, [h('span'), h('span'), h('span')]);
    const body = h('div', { class: 'card__body', id: 'body' });
    const foot = h('footer', { class: 'foot', id: 'foot' });
    const card = h('section', { class: 'card', id: 'card', 'aria-live': 'polite' }, [body, foot]);
    const overlay = h('div', { class: 'overlay', role: 'dialog', 'aria-label': 'Simpl Courses setup' }, [
      h('div', { class: 'bg', 'aria-hidden': 'true' }, ['a', 'b', 'c', 'd'].map((k) => h('i', { class: `blob blob--${k}` }))),
      h('main', { class: 'page' }, [
        h('header', { class: 'top' }, [h('span', { class: 'mark mark--sm', 'aria-hidden': 'true', html: MARK }), h('span', { class: 'top__name', text: 'Simpl Courses' }), stepLabel, skipBtn]),
        progress,
        card,
      ]),
    ]);
    shadow.append(h('style', { text: self.BCV_SETUP_CSS || '' }), overlay);
    ui = { host, overlay, card, body, foot, progress, stepLabel, skipBtn };
    html.classList.add('bcv-setup-open');
    (document.body || html).append(host);
    go(0, 1);
  }

  async function close() {
    if (!ui) return;
    const { host, overlay } = ui;
    ui = null;
    st = null;
    html.classList.remove('bcv-setup-open');
    if (!reduced()) {
      overlay.classList.add('is-closing');
      await new Promise((r) => setTimeout(r, 280));
    }
    host.remove();
  }

  // ---- the shell: progress, footer, transitions --------------------------------------------------
  function paintChrome() {
    ui.stepLabel.textContent = STEP_LABELS[st.step] || '';
    [...ui.progress.children].forEach((seg, i) => {
      seg.classList.toggle('is-done', i < st.step);
      seg.classList.toggle('is-current', i === st.step);
    });
  }

  /** Replace the card's content with the next step: the old one slides out, the card's height
   *  eases to the new size, the new one slides in. */
  async function transitionTo(build, dir = 1) {
    const { card, body, foot } = ui;
    const h0 = card.getBoundingClientRect().height;
    if (!reduced() && body.childElementCount) {
      body.style.setProperty('--leave', `${-16 * dir}px`);
      body.classList.add('is-leaving');
      await new Promise((r) => setTimeout(r, 170));
      if (!ui) return;
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
    if (!ui || reduced()) return;
    const { card } = ui;
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
    ui.card.classList.remove('is-shaking');
    void ui.card.offsetWidth;
    ui.card.classList.add('is-shaking');
  }

  function footer({ next = LABELS[st.step], onNext, disabled = false, back = st.step > 0 } = {}) {
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
    ui.foot.append(...[
      back ? h('button', { type: 'button', class: 'btn btn--ghost', id: 'back', text: 'Back', onclick: () => go(st.step - 1, -1) }) : null,
      h('span', { class: 'foot__spacer' }),
      nextBtn,
    ].filter(Boolean));
    return nextBtn;
  }

  const STEPS = [courses, grades, smart]; // function declarations below

  function go(n, dir = n > st.step ? 1 : -1) {
    st.step = Math.max(0, Math.min(STEPS.length - 1, n));
    transitionTo(STEPS[st.step], dir);
  }

  // ---- the courses ---------------------------------------------------------------------------------
  async function scan() {
    st.scanning = true;
    st.scanError = null;
    try {
      const [all, favs] = await Promise.all([store.courses({ force: true }), store.favorites({ force: true }).catch(() => [])]);
      const favIds = new Set((favs || []).map((c) => String(c.id)));
      const list = (all || []).filter((c) => c.state === 'current').map((c) => ({ id: String(c.id), code: c.code || c.name, name: c.name, originalName: c.originalName || c.name, nickname: c.nickname || '', color: c.color, favorite: !!c.favorite || favIds.has(String(c.id)) }));
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

  function courses() {
    const { body } = ui;
    const head = h('h1', { class: 'h1', text: 'Finding your courses' });
    const sub = h('p', { class: 'sub', text: 'Reading your enrollments.' });
    const wrap = h('div');
    body.append(head, sub, wrap);
    const nextBtn = footer({ disabled: true, onNext: () => go(1) });

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
        const toggle = (el) => {
          if (st.favs.has(c.id)) st.favs.delete(c.id); else st.favs.add(c.id);
          el.classList.toggle('is-on', st.favs.has(c.id));
          el.setAttribute('aria-checked', st.favs.has(c.id) ? 'true' : 'false');
          count.textContent = `${st.favs.size} selected`;
          allBtn.textContent = st.favs.size === list.length ? 'Clear' : 'All';
          nextBtn.disabled = st.favs.size === 0;
        };
        // a nickname is Canvas's own (it shows everywhere, in Canvas too); typed here, saved with the rest
        const nick = h('input', { class: 'row__nick', type: 'text', placeholder: 'Nickname', maxlength: '60', 'aria-label': `Nickname for ${c.originalName}`, value: st.nicks[c.id] ?? c.nickname, oninput: (e) => { st.nicks[c.id] = e.target.value; }, onclick: (e) => e.stopPropagation(), onkeydown: (e) => e.stopPropagation() });
        return h('div', { class: `row ${on ? 'is-on' : ''}`, dataset: { course: c.id }, role: 'checkbox', tabindex: '0', 'aria-checked': on ? 'true' : 'false', 'aria-label': c.originalName, onclick: (e) => toggle(e.currentTarget), onkeydown: (e) => { if (e.key === ' ' || e.key === 'Enter') { e.preventDefault(); toggle(e.currentTarget); } } }, [
          h('span', { class: 'row__dot', style: { background: c.color } }),
          h('span', { class: 'row__body' }, [h('span', { class: 'row__code', text: c.code }), h('span', { class: 'row__name', text: c.originalName })]),
          nick,
          h('span', { class: 'row__box' }, svg(CHECK, { size: 12, stroke: '#fff', width: 3 })),
        ]);
      }));
      stagger([...rows.children], 50);
      head.textContent = 'Which are you in?';
      sub.textContent = 'Unchecked courses stay hidden everywhere. A nickname replaces the name everywhere, in Canvas too.';
      wrap.replaceChildren(h('div', { class: 'listhead' }, [count, allBtn]), rows);
      nextBtn.disabled = st.favs.size === 0;
      settleHeight();
    };
    const drawEmpty = () => {
      head.textContent = st.scanError ? 'The courses could not be read' : 'No active courses';
      sub.textContent = st.scanError ? st.scanError : 'Between terms? Canvas lists nothing active right now.';
      wrap.replaceChildren(h('div', { class: 'empty' }, [
        h('span', { text: st.scanError ? 'Check that you are signed in to Canvas, then try again.' : 'You can finish setup now and choose courses later on the Courses page.' }),
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
      if (!ui || st.step !== 0) return;
      if (st.courses.length) draw(); else drawEmpty();
    }
    if (st.courses.length) draw(); else run();
  }

  // ---- grades --------------------------------------------------------------------------------------
  function grades() {
    const { body } = ui;
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
      h('div', { class: 'seg' }, GRADES.map((letter) => h('button', { type: 'button', class: `seg__b ${(st.targets[c.id] || 'A') === letter ? 'is-on' : ''}`, text: letter, onclick: (e) => {
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
    footer({ onNext: () => go(2) });
  }

  // ---- the smart panel -----------------------------------------------------------------------------
  function smart() {
    const { body } = ui;
    const settings = st.settings;
    const provider = () => PROVIDERS.find((p) => p.key === st.provider) || PROVIDERS[0];
    const input = h('input', { type: 'password', id: 'key', placeholder: provider().placeholder, autocomplete: 'off', spellcheck: 'false', 'aria-label': 'API key', value: st.key });
    const tick = svg(CHECK, { size: 16, stroke: '#34c759', width: 2.6, cls: 'tick' });
    tick.hidden = !st.keyOk;
    const field = h('div', { class: 'field field--key' }, [input, tick]);
    const result = h('div', { class: 'result', id: 'keyResult', text: st.keyMsg });
    const steps = h('div', { class: 'keysteps' });
    let notNow = null; // the quiet button in the footer, built below
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
      if (notNow) notNow.hidden = input.value.trim().length > 0;
    } }, [h('span', { class: 'prov__n', text: p.name }), h('span', { class: 'prov__s', text: p.note })])));
    stagger([...provs.children], 60);
    drawSteps();
    if (!st.key) st.key = settings.smart?.[provider().settingsKey] || '';
    input.value = st.key;
    body.append(
      h('h1', { class: 'h1', text: 'Smart panel' }),
      h('p', { class: 'sub', text: 'Optional. Runs on your own key, billed by the provider.' }),
      h('div', { class: 'notice notice--red', id: 'purpose', text: PURPOSE }),
      provs, field, result, steps,
    );
    notNow = h('button', { type: 'button', class: 'btn btn--quiet', id: 'notNow', text: 'Not now', onclick: () => { st.key = ''; st.keyOk = false; finish({ skipped: false }); } });
    const nextBtn = footer({
      onNext: async () => {
        const key = input.value.trim();
        if (!key) { await finish({ skipped: false }); return; }
        const p = provider();
        result.textContent = 'Checking the key…';
        result.className = 'result';
        // one cheap call before saving, so a typo surfaces here rather than mid-semester
        const r = await BCV.smartClient.send({ type: 'testKey', provider: p.key, key });
        if (r?.ok) {
          st.key = key;
          st.keyOk = true;
          st.keyMsg = r.message || 'The key works.';
          await S.update({ smart: { [p.settingsKey]: key, preferred: p.key } });
          tick.hidden = false;
          result.textContent = st.keyMsg;
          result.className = 'result is-ok';
          await new Promise((res) => setTimeout(res, 350));
          await finish({ skipped: false });
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

  // ---- done: save, close, tour ---------------------------------------------------------------------
  /** Everything the steps decided, where the screens read it: favourites through Canvas (the one
   *  list every screen follows), the grade preferences under this host, the "done" flags the popup
   *  and the first-run check read. Then the card shows a check for a moment, closes, and the tour
   *  starts on this page. Skipping writes only the flags, and no tour. */
  async function finish({ skipped = false } = {}) {
    if (!st || st.closing) return;
    st.closing = true;
    const { app } = st;
    ui.skipBtn.hidden = true;
    let favChanged = false;
    try {
      await store.setPref('setupDone', true);
      if (!skipped) {
        const [targetsPref] = await Promise.all([store.pref('gradeTargets')]);
        const targets = { ...((targetsPref && typeof targetsPref === 'object') ? targetsPref : {}) };
        for (const c of st.courses) if (st.favs.has(c.id)) targets[c.id] = GRADES.includes(st.targets[c.id]) ? st.targets[c.id] : 'A';
        await Promise.all([
          store.setPref('gpaGoal', st.goal),
          store.setPref('gpaTracking', st.tracking ? { priorGpa: null, priorCourses: 0, since: new Date().toISOString().slice(0, 10) } : null),
          store.setPref('gradeTargets', targets),
        ]);
        const changes = st.courses.filter((c) => c.favorite !== st.favs.has(c.id));
        for (const c of changes) await store.setFavorite(c.id, st.favs.has(c.id)).catch(() => {});
        const renamed = st.courses.filter((c) => c.id in st.nicks && String(st.nicks[c.id]).trim() !== (c.nickname || ''));
        for (const c of renamed) await store.setNickname(c.id, st.nicks[c.id]).catch(() => {});
        favChanged = changes.length > 0 || renamed.length > 0;
      }
      await BCV.api.storage.local.set({ 'setup:done': true, 'setup:offered': true });
    } catch (e) {
      console.error('[Simpl Courses setup]', e);
    }
    if (!skipped && ui) {
      // a moment of "all set" before the page takes over
      const check = h('span', { class: 'done__check', 'aria-hidden': 'true', html: '<svg viewBox="0 0 24 24" width="31" height="31" fill="none" stroke="#34c759" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path class="arc" style="--len:30" d="M20 6L9 17l-5-5" stroke-dasharray="30"/></svg>' });
      st.step = STEPS.length; // past the last step: every segment done, the label "Done"
      await transitionTo(() => {
        ui.body.append(h('div', { class: 'done' }, [check, h('h1', { class: 'h1', text: 'All set' }), h('p', { class: 'sub', text: 'Now, a quick tour.' })]));
      }, 1);
      await new Promise((r) => setTimeout(r, reduced() ? 150 : 1100));
    }
    await close();
    if (favChanged) {
      app.loadShellData({ force: true });
      await app.render();
    }
    if (!skipped && BCV.tour) await BCV.tour.start(app);
  }

  BCV.setup = { open, close, active, PROVIDERS };
})();
