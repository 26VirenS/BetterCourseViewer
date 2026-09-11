/* Guided setup (?bcv=setup): six short steps on the student's own Canvas page, so every
 * choice is applied through Canvas or saved where the screens read it.
 *   1 Welcome · 2 the site · 3 courses (favourites, which every screen follows) ·
 *   4 grades (goal, tracking, a target per favourite course) · 5 the smart panel (optional) ·
 *   6 the tour.
 * Reached from Settings → Guided setup, the toolbar popup, the account sheet on a phone, the
 * options page after install (Safari and Chrome), and the app's first launch (iOS). */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const S = BCV.settings;
  const store = BCV.store;
  const html = document.documentElement;

  const STEPS = ['Welcome', 'Your site', 'Your courses', 'Your grades', 'Smart panel', 'Tour'];
  const KEY_HELP = {
    claude: { name: 'Claude', url: 'https://console.anthropic.com/settings/keys', host: 'console.anthropic.com', placeholder: 'sk-ant-…', key: 'claudeKey', steps: ['Open Settings → API keys and choose Create key.', 'Copy the key: it is shown once.', 'Paste it below and press Test.'] },
    openai: { name: 'ChatGPT', url: 'https://platform.openai.com/api-keys', host: 'platform.openai.com', placeholder: 'sk-…', key: 'openaiKey', steps: ['Open API keys and choose Create new secret key.', 'Copy the key: it is shown once.', 'Paste it below and press Test.'] },
  };
  const gpa2 = (n) => (n === null || n === undefined ? '—' : n.toFixed(2));
  const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
  const native = () => self.BCVBridge?.native || null;

  async function render(ctx) {
    const { app } = ctx;
    const G = BCV.screens.gpa;
    const phone = !!BCV.phone?.active();
    html.classList.add('bcv-setup');
    let step = clamp(Number(ctx.route.params.get('step')) || 0, 0, STEPS.length - 1);
    const screen = U.el('bcv-screen bcv-su');
    const card = U.el('bcv-su__card');
    screen.append(card);

    // what the steps share
    const state = { courses: null, favs: null, chosen: null, goal: 3.7, tracking: null, targets: {}, smartSaved: false };
    const finish = async () => {
      await store.setPref('setupDone', true);
      await BCV.api.storage.local.set({ 'setup:done': true }).catch(() => {});
    };
    const leave = async (href = '/') => { await finish(); app.go(href); };

    const progress = () => U.el('bcv-su__progress', [
      U.el('bcv-su__dots', STEPS.map((label, i) => h('span', { class: `bcv-su__dot ${i < step ? 'is-done' : ''} ${i === step ? 'is-current' : ''}`, title: label }))),
      U.text('bcv-su__stepno', `Step ${step + 1} of ${STEPS.length} · ${STEPS[step]}`, 'span'),
    ]);
    const footer = ({ next = 'Continue', onNext, back = true, skip = null, busy = null } = {}) => {
      const nextBtn = h('button', { type: 'button', class: 'bcv-su__btn is-primary', text: next, onclick: async () => {
        nextBtn.disabled = true;
        if (busy) nextBtn.textContent = busy;
        try {
          if (await onNext() !== false) go(step + 1);
        } catch (e) {
          U.toast(e?.message || 'That did not work', { error: true });
        }
        nextBtn.disabled = false;
        nextBtn.textContent = next;
      } });
      return U.el('bcv-su__foot', [
        back && step > 0 ? h('button', { type: 'button', class: 'bcv-su__btn', text: 'Back', onclick: () => go(step - 1) }) : null,
        skip ? h('button', { type: 'button', class: 'bcv-su__link', text: skip.label, onclick: skip.onSelect }) : null,
        h('span', { class: 'bcv-su__spacer' }),
        nextBtn,
      ]);
    };
    const skipLink = () => h('button', { type: 'button', class: 'bcv-su__skip', text: 'Skip setup', onclick: () => leave('/') });
    const head = (icon, title, sub) => U.el('bcv-su__head', [
      h('span', { class: 'bcv-su__icon' }, U.svg(icon, { size: 22, width: 1.9 })),
      h('h1', { class: 'bcv-su__h1', text: title }),
      sub ? U.text('bcv-su__sub bcv-pretty', sub) : null,
    ]);

    function go(n) {
      step = clamp(n, 0, STEPS.length - 1);
      history.replaceState({ bcv: true }, '', `${location.pathname}?bcv=setup&step=${step}`);
      draw();
    }

    // ---- 1 Welcome ---------------------------------------------------------------------------------
    function welcome() {
      card.replaceChildren(
        U.el('bcv-su__hero', [
          h('span', { class: 'bcv-su__mark' }, U.svg(IC.sparkle, { size: 30, stroke: '#fff', width: 1.8 })),
          h('h1', { class: 'bcv-su__title', text: 'Welcome to Simpl Courses' }),
          U.text('bcv-su__lead bcv-pretty', `A calmer ${app.siteName()}: your day, your courses and your grades on one clean screen, with everything still coming from Canvas.`),
        ]),
        U.el('bcv-su__list', [
          ['Your site', 'Simpl Courses turns on for your school\'s Canvas.'],
          ['Your courses', 'Choose the courses you want to see everywhere.'],
          ['Your grades', 'A term GPA goal, tracking, and a target for every course.'],
          ['Smart panel', 'Optional: add a key and the panel reads the page you are on.'],
          ['Tour', 'A quick walk through what changed.'],
        ].map(([t, s], i) => U.el('bcv-su__item', [h('span', { class: 'bcv-su__num', text: String(i + 1) }), h('div', {}, [U.text('bcv-su__item-t', t), U.text('bcv-su__item-s', s)])]))),
        footer({ next: 'Get started', onNext: () => true, back: false, skip: { label: 'Skip setup', onSelect: () => leave('/') } }),
      );
    }

    // ---- 2 the site --------------------------------------------------------------------------------
    function site() {
      const host = location.hostname;
      const inApp = !!native();
      const builtIn = /\.instructure\.com$/i.test(host);
      card.replaceChildren(
        progress(),
        head(IC.check, `On for ${host}`, inApp
          ? 'This app is signed in to your Canvas. Everything you see from here on is drawn from your own Canvas data, on this device.'
          : builtIn
            ? 'Every *.instructure.com site is on automatically, so nothing to do here. Add a school-hosted Canvas address any time in Settings → Canvas sites.'
            : 'This site was added in Settings → Canvas sites, so Simpl Courses runs on it. The toolbar button shows the same for any Canvas page you are on.'),
        U.el('bcv-su__note', [
          U.svg(IC.lock, { size: 15, stroke: 'var(--bcv-ink3)', width: 1.9 }),
          U.text('bcv-su__note-t bcv-pretty', 'Simpl Courses reads Canvas with your own signed-in session. Nothing about you passes through any other server.', 'span'),
        ]),
        footer({ onNext: () => true }),
      );
    }

    // ---- 3 courses ---------------------------------------------------------------------------------
    async function courses() {
      card.replaceChildren(progress(), head(IC.book, 'Your courses', 'Scanning your enrolments…'), U.loading('rows', 4));
      if (!state.courses) {
        const [all, favs] = await Promise.all([store.courses({ force: true }).catch(() => null), store.favorites().catch(() => [])]);
        if (!ctx.alive()) return;
        state.courses = all;
        state.favs = favs;
        if (all) {
          const starred = all.filter((c) => c.favorite).map((c) => c.id);
          // until something is starred Canvas shows every current course; start from what is shown
          state.chosen = new Set(starred.length ? starred : all.filter((c) => c.state === 'current').map((c) => c.id));
        }
      }
      if (!state.courses) {
        card.replaceChildren(progress(), head(IC.book, 'Your courses', 'Your courses could not be loaded.'), footer({ onNext: () => true, next: 'Skip this step' }));
        return;
      }
      const current = state.courses.filter((c) => c.state === 'current');
      const others = state.courses.filter((c) => c.state !== 'current');
      const countText = () => `${U.plural(state.chosen.size, 'course')} chosen`;
      const count = U.text('bcv-su__count', countText(), 'span');
      const row = (c) => {
        const sw = U.switchEl(state.chosen.has(c.id), (on) => { if (on) state.chosen.add(c.id); else state.chosen.delete(c.id); count.textContent = countText(); }, `Show ${c.name} everywhere`);
        return U.el('bcv-su__course', [
          h('span', { class: 'bcv-su__dot-c', style: { background: c.color } }),
          h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-su__course-n bcv-ellip', c.name), U.text('bcv-su__course-s bcv-ellip', [c.code !== c.name ? c.code : null, c.term, c.state !== 'current' ? c.state : null].filter(Boolean).join(' · '))]),
          sw,
        ]);
      };
      const otherList = U.el('bcv-su__courses', others.map(row));
      otherList.hidden = true;
      card.replaceChildren(
        progress(),
        head(IC.book, 'Your courses', `${U.plural(state.courses.length, 'course')} found. The ones you keep on are your favourites in Canvas, and they are what every screen follows: Dashboard, Courses, To Do, Calendar, Grades and the GPA.`),
        U.el('bcv-su__rowhead', [U.text('bcv-su__k', 'Current term', 'span'), count]),
        U.el('bcv-su__courses', current.length ? current.map(row) : [U.text('bcv-su__empty', 'No current courses.')]),
        others.length ? h('button', { type: 'button', class: 'bcv-su__disclose', text: `${U.plural(others.length, 'other course')} (past or not started)`, onclick: (e) => { otherList.hidden = !otherList.hidden; e.currentTarget.classList.toggle('is-open', !otherList.hidden); } }) : null,
        otherList,
        U.text('bcv-su__hint bcv-pretty', 'Change this any time: star a course on the Courses page, or in Canvas itself. Both are the same list.'),
        footer({
          busy: 'Saving…',
          onNext: async () => {
            const changes = state.courses.filter((c) => c.favorite !== state.chosen.has(c.id));
            for (const c of changes) await store.setFavorite(c.id, state.chosen.has(c.id));
            if (changes.length) {
              state.courses = await store.courses({ force: true }).catch(() => state.courses);
              app.loadShellData({ force: true });
              U.toast(`Favourites saved · ${U.plural(state.chosen.size, 'course')}`);
            }
            return true;
          },
        }),
      );
    }

    // ---- 4 grades ----------------------------------------------------------------------------------
    async function grades() {
      card.replaceChildren(progress(), head(IC.chart, 'Your grades', 'Loading your scores…'), U.loading('rows', 4));
      const [all, goalPref, trackingPref, targetsPref] = await Promise.all([state.courses ? Promise.resolve(state.courses) : store.courses().catch(() => null), store.pref('gpaGoal'), store.pref('gpaTracking'), store.pref('gradeTargets')]);
      if (!ctx.alive()) return;
      state.courses = all;
      if (Number.isFinite(goalPref)) state.goal = goalPref;
      if (trackingPref && typeof trackingPref === 'object' && (trackingPref.since || Number.isFinite(trackingPref.priorGpa))) state.tracking = trackingPref;
      if (targetsPref && typeof targetsPref === 'object') state.targets = { ...targetsPref };
      const chosen = (all || []).filter((c) => c.state === 'current' && (state.chosen ? state.chosen.has(c.id) : c.favorite || !all.some((x) => x.favorite)));
      const scored = chosen.filter((c) => c.score !== null && c.score !== undefined);
      const termGpa = scored.length ? scored.reduce((s, c) => s + G.pointsFor(c.grade, Number(c.score)), 0) / scored.length : null;

      // the goal
      let goal = state.goal;
      const goalVal = U.text('bcv-su__goal-v', gpa2(goal), 'span');
      const goalGap = U.text('bcv-su__course-s bcv-pretty', '');
      const syncGoal = () => {
        goalVal.textContent = gpa2(goal);
        goalGap.textContent = termGpa === null ? 'Term GPA is the plain average of your course letter grades on a 4.0 scale.' : termGpa >= goal ? `Your term GPA is ${gpa2(termGpa)} today, ${gpa2(termGpa - goal)} above this goal.` : `Your term GPA is ${gpa2(termGpa)} today, ${gpa2(goal - termGpa)} below this goal.`;
      };
      syncGoal();
      const goalRow = U.el('bcv-su__block', [
        U.el('bcv-su__rowhead', [U.text('bcv-su__k', 'Term GPA goal', 'span')]),
        U.el('bcv-su__goal', [
          h('button', { type: 'button', class: 'bcv-su__step', text: '−', 'aria-label': 'Lower the goal', onclick: () => { goal = clamp(+(goal - 0.05).toFixed(2), 0, 4); syncGoal(); } }),
          goalVal,
          h('button', { type: 'button', class: 'bcv-su__step', text: '+', 'aria-label': 'Raise the goal', onclick: () => { goal = clamp(+(goal + 0.05).toFixed(2), 0, 4); syncGoal(); } }),
        ]),
        goalGap,
      ]);

      // tracking
      let on = !!state.tracking;
      const priorGpa = h('input', { class: 'bcv-input bcv-su__input', id: 'bcv-su-prior', type: 'number', min: '0', max: '4', step: '0.01', placeholder: '3.42', inputmode: 'decimal', value: Number.isFinite(state.tracking?.priorGpa) ? String(state.tracking.priorGpa) : '' });
      const priorN = h('input', { class: 'bcv-input bcv-su__input', id: 'bcv-su-prior-n', type: 'number', min: '1', max: '200', step: '1', placeholder: '8', inputmode: 'numeric', value: state.tracking?.priorCourses > 0 ? String(state.tracking.priorCourses) : '' });
      const fields = U.el('bcv-su__fields', [
        h('label', { class: 'bcv-su__field' }, [h('span', { text: 'GPA before this term' }), priorGpa]),
        h('label', { class: 'bcv-su__field' }, [h('span', { text: 'Courses it covers' }), priorN]),
      ]);
      fields.hidden = !on;
      const trackRow = U.el('bcv-su__block', [
        U.el('bcv-su__switchrow', [
          h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-su__k', 'Track my GPA over time'), U.text('bcv-su__course-s bcv-pretty', 'Canvas stores no GPA and no history. The Grades page keeps one snapshot a day from now on; your record before this term is optional.')]),
          U.switchEl(on, (v) => { on = v; fields.hidden = !v; if (v) priorGpa.focus(); }, 'Track my GPA over time'),
        ]),
        fields,
      ]);

      // a target per course
      const pick = {};
      const targetRows = chosen.map((c) => {
        const pct = c.score !== null && c.score !== undefined ? Number(c.score) : null;
        const cur = pct === null ? null : (c.grade ? String(c.grade).replace(/-/g, '−') : G.letterFor(pct)[0]);
        const found = cur === null ? -1 : G.SCALE.findIndex((s) => s[0] === cur);
        const idx = Number.isInteger(state.targets[c.id]) ? state.targets[c.id] : found >= 0 ? found : 1; // A− when nothing is scored yet
        pick[c.id] = clamp(idx, 0, G.SCALE.length - 1);
        const sel = h('select', { class: 'bcv-select bcv-su__select', 'aria-label': `Target grade for ${c.name}`, onchange: (e) => { pick[c.id] = Number(e.target.value); } }, G.SCALE.map((s, i) => h('option', { value: String(i), selected: i === pick[c.id] ? true : null, text: `${s[0]} · ${s[1]}%+` })));
        return U.el('bcv-su__course', [
          h('span', { class: 'bcv-su__dot-c', style: { background: c.color } }),
          h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-su__course-n bcv-ellip', c.shortName || c.name), U.text('bcv-su__course-s', pct === null ? 'No score yet' : `${store.fmtPts(pct)}% today · ${cur}`)]),
          sel,
        ]);
      });
      card.replaceChildren(
        progress(),
        head(IC.chart, 'Your grades', `The Grades page adds a term GPA on the 4.0 scale from the scores Canvas reports${chosen.length ? `, across your ${U.plural(chosen.length, 'chosen course')}` : ''}. Set what you are aiming for; every number stays traceable to a Canvas field.`),
        goalRow,
        trackRow,
        U.el('bcv-su__block', [
          U.el('bcv-su__rowhead', [U.text('bcv-su__k', 'A target for every course', 'span'), U.text('bcv-su__count', 'the Grades page shows what each still needs', 'span')]),
          U.el('bcv-su__courses', targetRows.length ? targetRows : [U.text('bcv-su__empty', 'No chosen courses yet.')]),
        ]),
        footer({
          busy: 'Saving…',
          onNext: async () => {
            let tracking = null;
            if (on) {
              const blank = !priorGpa.value.trim() && !priorN.value.trim();
              const g = Number(priorGpa.value), n = Math.round(Number(priorN.value));
              if (!blank && (!(g >= 0 && g <= 4) || !(n >= 1))) {
                U.toast('Enter your GPA before this term and how many courses it covers, or leave both blank.', { error: true });
                priorGpa.focus();
                return false;
              }
              const since = state.tracking?.since || new Date().toISOString().slice(0, 10);
              tracking = blank ? { priorGpa: null, priorCourses: 0, since } : { priorGpa: g, priorCourses: n, since };
            }
            state.goal = goal;
            state.tracking = tracking;
            state.targets = { ...state.targets, ...pick };
            await Promise.all([store.setPref('gpaGoal', goal), store.setPref('gpaTracking', tracking), store.setPref('gradeTargets', state.targets)]);
            U.toast('Grades set up');
            return true;
          },
        }),
      );
    }

    // ---- 5 the smart panel --------------------------------------------------------------------------
    async function smart() {
      const settings = await S.get();
      if (!ctx.alive()) return;
      let provider = settings.smart.openaiKey && !settings.smart.claudeKey ? 'openai' : 'claude';
      const body = U.el('bcv-su__block');
      const result = U.text('bcv-su__result', '');
      const keyInput = h('input', { class: 'bcv-input bcv-su__input bcv-su__key', type: 'password', autocomplete: 'off', spellcheck: 'false', 'aria-label': 'API key' });
      const reveal = h('button', { type: 'button', class: 'bcv-su__btn bcv-su__btn--sm', text: 'Show', onclick: () => { const show = keyInput.type === 'password'; keyInput.type = show ? 'text' : 'password'; reveal.textContent = show ? 'Hide' : 'Show'; } });
      const testBtn = h('button', { type: 'button', class: 'bcv-su__btn bcv-su__btn--sm', text: 'Test', onclick: async () => {
        const key = keyInput.value.trim();
        if (!key) { result.textContent = 'Paste a key first.'; result.className = 'bcv-su__result is-err'; return; }
        testBtn.disabled = true;
        result.textContent = 'Testing…';
        result.className = 'bcv-su__result';
        await S.update({ smart: { [KEY_HELP[provider].key]: key } });
        const r = await BCV.smartClient.send({ type: 'testKey', provider, key });
        result.textContent = r?.message || 'No answer from the extension.';
        result.className = `bcv-su__result ${r?.ok ? 'is-ok' : 'is-err'}`;
        testBtn.disabled = false;
      } });
      const cardFor = (id) => {
        const p = KEY_HELP[id];
        const active = provider === id;
        return h('button', { type: 'button', class: `bcv-su__prov ${active ? 'is-active' : ''}`, dataset: { provider: id }, onclick: () => { provider = id; drawBody(); } }, [
          U.text('bcv-su__prov-n', p.name), U.text('bcv-su__prov-s', settings.smart[p.key] ? 'Key saved' : 'Bring your own key'),
        ]);
      };
      function drawBody() {
        const p = KEY_HELP[provider];
        keyInput.placeholder = p.placeholder;
        keyInput.value = settings.smart[p.key] || '';
        result.textContent = '';
        body.replaceChildren(
          U.el('bcv-su__provs', [
            cardFor('claude'), cardFor('openai'),
            h('span', { class: 'bcv-su__prov is-soon', 'aria-disabled': 'true' }, [U.text('bcv-su__prov-n', 'Gemini'), U.text('bcv-su__prov-s', 'Coming soon')]),
          ]),
          U.el('bcv-su__rowhead', [U.text('bcv-su__k', `How to get a ${p.name} key`, 'span')]),
          h('ol', { class: 'bcv-su__ol' }, [
            h('li', {}, ['Sign in (or create an account) at ', h('a', { href: p.url, target: '_blank', rel: 'noopener', text: p.host }), '.']),
            ...p.steps.map((s) => h('li', { text: s })),
          ]),
          U.el('bcv-su__keyrow', [keyInput, reveal, testBtn]),
          result,
          U.text('bcv-su__hint bcv-pretty', 'Requests go straight from this device to the provider with your key; nothing passes through any other server. Usage is billed by the provider on your key.'),
        );
      }
      drawBody();
      card.replaceChildren(
        progress(),
        head(IC.sparkle, 'The smart panel', 'Optional. With a key, the panel on every page can summarize what is due, explain an assignment, or plan your week from what is on the screen. Without one, the panel stays quiet and everything else works.'),
        body,
        footer({
          next: 'Save and continue',
          busy: 'Saving…',
          skip: { label: 'Skip for now', onSelect: () => go(step + 1) },
          onNext: async () => {
            const key = keyInput.value.trim();
            await S.update({ smart: { [KEY_HELP[provider].key]: key, preferred: provider } });
            state.smartSaved = !!key;
            U.toast(key ? `${KEY_HELP[provider].name} key saved` : 'No key saved');
            return true;
          },
        }),
      );
    }

    // ---- 6 the tour --------------------------------------------------------------------------------
    function tour() {
      const items = phone
        ? ['Today: the counters, the day\'s list, the week load', 'The five tabs, and Inbox, Groups and Settings under your avatar', 'Courses, To Do and the Calendar', 'Grades: the term GPA, your goal and a card per course', 'A course: the chip row, Next up, the reader', 'A course\'s full grade page, with what-if scores']
        : ['The dashboard: the counters, what is due, the courses', 'The sidebar, the smart button and the appearance switch', 'Courses, To Do and the Calendar', 'Grades: the term GPA, your goal, tracking, a card per course', 'A course: the rail, the reader, Next up', 'A course\'s full grade page, with what-if scores'];
      card.replaceChildren(
        progress(),
        head(IC.cal, 'Take the tour', `You are set up. A short tour points out what changed, on the real pages (${U.plural(BCV.tour?.count?.(phone) || items.length, 'stop')}, one tap each).`),
        U.el('bcv-su__list', items.map((t, i) => U.el('bcv-su__item', [h('span', { class: 'bcv-su__num', text: String(i + 1) }), h('div', {}, [U.text('bcv-su__item-t', t)])]))),
        footer({
          next: 'Start the tour',
          skip: { label: 'Finish without the tour', onSelect: () => leave('/') },
          onNext: async () => {
            await finish();
            await BCV.tour.start(app);
            return false;
          },
        }),
      );
    }

    async function draw() {
      card.classList.remove('bcv-enter');
      void card.offsetWidth; // restart the entrance
      card.classList.add('bcv-enter');
      const fn = [welcome, site, courses, grades, smart, tour][step];
      await fn();
      if (!ctx.alive()) return;
      if (step > 0 && step < STEPS.length - 1) card.append(skipLink());
      window.scrollTo(0, 0);
    }
    await draw();
    ctx.setSmart({ label: 'Guided setup', actions: [], context: () => `Simpl Courses guided setup, step ${step + 1} of ${STEPS.length}: ${STEPS[step]}` });
    return screen;
  }

  BCV.screens = BCV.screens || {};
  BCV.screens.setup = { render, STEPS };
})();
