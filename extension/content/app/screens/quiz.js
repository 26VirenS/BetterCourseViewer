/* Quiz taking, drawn to the mockup: chrome hidden, an intro card, one
 * question at a time or all questions on one scroll, progress pills that
 * jump, flags, an elapsed/remaining pill, a review-before-submit screen and
 * a submitted screen. Every action is a Canvas quiz-submission API call and
 * the quiz's own settings (one at a time, no going back, time limit, access
 * code) are honoured. Nothing is ever auto-submitted. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h, htmlToText } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;
  const LETTERS = 'ABCDEFGHIJKLMNOP';
  const FLAG = 'M6 4v16M6 4h11l-2 4 2 4H6';
  const CHECK = 'M20 6L9 17l-5-5';
  const MODE_ONE = 'M5 7h14v10H5z';
  const MODE_ALL = 'M4 5h16M4 10h16M4 15h16M4 20h10';

  const pad = (n) => String(n).padStart(2, '0');
  const clock = (ms) => {
    const s = Math.max(0, Math.round(ms / 1000));
    const hh = Math.floor(s / 3600), mm = Math.floor((s % 3600) / 60), ss = s % 60;
    return hh ? `${hh}:${pad(mm)}:${pad(ss)}` : `${mm}:${pad(ss)}`;
  };
  const answered = (v) => !(v === null || v === undefined || v === '' || (Array.isArray(v) && !v.length));
  const CHOICE = new Set(['multiple_choice_question', 'true_false_question']);
  const MULTI = new Set(['multiple_answers_question']);
  const TEXT = new Set(['short_answer_question', 'essay_question', 'numerical_question']);
  const INFO = new Set(['text_only_question']);

  async function render(ctx, course) {
    const { app, route } = ctx;
    const cid = course.id;
    const qid = route.arg;
    const quizUrl = `${course.url}/quizzes/${qid}`;
    const html = document.documentElement;
    html.classList.add('bcv-quiz'); // hides the sidebar and the smart button; app.js clears it on the next render

    const st = { stage: 'intro', idx: 0, mode: 'one', quiz: null, sub: null, questions: [], flags: {}, saving: 0, savedAt: 0, timer: null, warned: {}, done: null, code: '' };
    const screen = U.el('bcv-qz');
    screen.append(U.loading('Loading the quiz…'));

    const [quiz, subs] = await Promise.all([store.quiz(cid, qid, { force: true }).catch(() => null), store.quizSubmissions(cid, qid, { force: true }).catch(() => [])]);
    if (!ctx.alive()) return screen;
    if (!quiz) {
      screen.replaceChildren(U.el('bcv-qz__intro', [U.errorBox('This quiz could not be loaded.'), h('button', { type: 'button', class: 'bcv-qz__big', text: 'Back to the course', onclick: () => app.go(course.url, { confirmed: true }) })]));
      return screen;
    }
    st.quiz = quiz;
    st.sub = (subs || []).find((s) => s.workflow_state === 'untaken') || null;
    st.mode = quiz.one_question_at_a_time ? 'one' : (await store.pref('quizMode', 'one'));
    const forcedOne = !!quiz.one_question_at_a_time;
    const noBack = !!quiz.cant_go_back;
    const timed = !!quiz.time_limit;
    const attemptsLeft = quiz.allowed_attempts && quiz.allowed_attempts > 0 ? quiz.allowed_attempts - (subs || []).filter((s) => s.workflow_state !== 'untaken').length : null;

    // ---- leave guard ---------------------------------------------------------------
    // app.go() asks before leaving while quizOpen is set (and clears it once you
    // say yes), so the browser-level guard only catches closing the tab, the back
    // button and typed URLs.
    const onUnload = (e) => {
      if (!app.state.quizOpen || !ctx.alive()) return;
      e.preventDefault();
      e.returnValue = '';
    };
    window.addEventListener('beforeunload', onUnload);
    const setOpen = (open) => {
      app.state.quizOpen = open && ctx.alive();
    };
    const leave = () => {
      setOpen(false);
      clearInterval(st.timer);
      app.go(quizUrl, { confirmed: true });
    };

    // ---- header -------------------------------------------------------------------
    const dueDay = quiz.due_at ? U.DAYS_LONG[U.parse(quiz.due_at).getDay()] : 'No due date';
    const timerLabel = h('span', { class: 'bcv-qz__clock', text: '0:00' });
    const modeWrap = h('div', { class: 'bcv-seg bcv-qz__modes' });
    const progressWrap = h('div');
    const head = U.el('bcv-qz__head', [
      U.el('bcv-qz__headrow', [
        h('button', { type: 'button', class: 'bcv-qz__exit', title: 'Save and exit', 'aria-label': 'Save and exit', onclick: leave }, U.svg(IC.close, { size: 16, width: 2.2 })),
        h('div', { class: 'bcv-qz__titles' }, [U.text('bcv-qz__title bcv-ellip', quiz.title), U.text('bcv-qz__sub', `${course.name} · ${dueDay}`)]),
        modeWrap,
        U.el('bcv-qz__timer', [U.svg('M12 5a8 8 0 100 16 8 8 0 000-16zM12 9v4l3 2', { size: 13, stroke: 'var(--bcv-ink3)', width: 2 }), timerLabel]),
      ]),
      progressWrap,
    ]);
    const body = h('div', { class: 'bcv-qz__body' });
    screen.replaceChildren(head, body);

    function tick() {
      const sub = st.sub;
      if (!sub) {
        timerLabel.textContent = timed ? `${quiz.time_limit} min` : '0:00';
        return;
      }
      if (timed && sub.end_at) {
        const left = U.parse(sub.end_at) - Date.now();
        timerLabel.textContent = clock(left);
        timerLabel.classList.toggle('is-low', left < 5 * 60e3);
        if (left <= 5 * 60e3 && left > 4.9 * 60e3 && !st.warned[5]) { st.warned[5] = true; U.toast('5 minutes left.'); }
        if (left <= 60e3 && left > 50e3 && !st.warned[1]) { st.warned[1] = true; U.toast('1 minute left.', { error: true }); }
        if (left <= 0 && !st.warned[0] && st.stage === 'take') { st.warned[0] = true; timeUp(); }
      } else {
        const started = U.parse(sub.started_at);
        timerLabel.textContent = clock(started ? Date.now() - started : (sub.time_spent || 0) * 1000);
      }
    }
    st.timer = setInterval(tick, 1000);
    tick();

    // ---- state helpers -------------------------------------------------------------
    const cur = () => st.questions[Math.min(st.idx, st.questions.length - 1)];
    const isAnswered = (q) => INFO.has(q.question_type) || answered(q.answer);
    const answeredCount = () => st.questions.filter(isAnswered).length;
    const answeredLabel = () => `${answeredCount()} of ${st.questions.length} answered`;
    const saveState = () => (st.saving > 0 ? 'Saving…' : st.savedAt ? 'Saved' : '');

    async function save(q, answer) {
      q.answer = answer;
      st.saving++;
      paintFooter();
      try {
        await store.quizApi.answer(st.sub, q.id, answer);
        st.savedAt = Date.now();
      } catch (e) {
        U.toast(`Could not save that answer: ${e.message}`, { error: true });
      } finally {
        st.saving--;
        paintFooter();
        paintProgress();
      }
    }
    const textTimers = new Map();
    function saveText(q, value) {
      q.answer = value;
      clearTimeout(textTimers.get(q.id));
      textTimers.set(q.id, setTimeout(() => save(q, value), 600));
    }
    async function toggleFlag(q) {
      const on = !q.flagged;
      q.flagged = on;
      paintProgress();
      try {
        await store.quizApi.flag(st.sub, q.id, on);
      } catch (e) {
        q.flagged = !on;
        paintProgress();
        U.toast(`Could not flag it: ${e.message}`, { error: true });
      }
      draw();
    }

    // ---- stages --------------------------------------------------------------------
    function draw() {
      if (!ctx.alive()) {
        clearInterval(st.timer);
        window.removeEventListener('beforeunload', onUnload);
        return;
      }
      modeWrap.replaceChildren(...(st.stage === 'take' && !forcedOne ? [
        modeBtn('one', 'One question at a time', MODE_ONE),
        modeBtn('all', 'Scroll through all questions', MODE_ALL),
      ] : []));
      modeWrap.hidden = !(st.stage === 'take' && !forcedOne);
      paintProgress();
      if (st.stage === 'intro') body.replaceChildren(intro());
      else if (st.stage === 'take') body.replaceChildren(st.mode === 'all' ? takeAll() : takeOne());
      else if (st.stage === 'review') body.replaceChildren(review());
      else body.replaceChildren(done());
      setOpen(st.stage === 'take' || st.stage === 'review');
      ctx.setSmart({
        label: `${quiz.title} · quiz`,
        actions: [],
        context: () => `Quiz: ${quiz.title} (${course.name}). ${st.questions.length} questions. This is an open attempt; the smart panel must not answer quiz questions for the student.`,
      });
    }
    function modeBtn(key, label, icon) {
      return h('button', { type: 'button', class: `bcv-qz__mode ${st.mode === key ? 'is-active' : ''}`, title: label, 'aria-label': label, onclick: () => { st.mode = key; store.setPref('quizMode', key); draw(); } }, U.svg(icon, { size: 15, width: 1.9 }));
    }

    function paintProgress() {
      if (st.stage !== 'take') {
        progressWrap.replaceChildren();
        return;
      }
      const all = st.mode === 'all';
      progressWrap.replaceChildren(U.el(`bcv-qz__progress ${all ? 'bcv-qz__progress--all' : ''}`, st.questions.map((q, k) => {
        const current = k === st.idx;
        const locked = noBack && k < st.idx;
        return h('button', {
          type: 'button',
          class: `bcv-qz__pill ${current ? 'is-current' : ''} ${isAnswered(q) ? 'is-answered' : ''} ${q.flagged ? 'is-flagged' : ''}`,
          disabled: locked || null,
          title: `Question ${k + 1}${q.flagged ? ' · flagged' : ''}`,
          onclick: () => {
            if (locked) return;
            st.idx = k;
            if (all) {
              const el = document.getElementById(`bcv-q${k}`);
              if (el) window.scrollTo({ top: window.scrollY + el.getBoundingClientRect().top - 130, behavior: 'smooth' });
              paintProgress();
            } else draw();
          },
        }, String(k + 1));
      })));
    }

    function intro() {
      const pts = quiz.points_possible !== null && quiz.points_possible !== undefined ? `${store.fmtPts(quiz.points_possible)} points` : 'ungraded';
      const bullets = [
        ['#34c759', CHECK, 'Answers save as you pick them. You can leave and come back.'],
        timed ? ['#ff9500', IC.warn, `Time limit: ${quiz.time_limit} minutes. The clock starts when you begin and keeps running if you leave.`] : null,
        forcedOne ? ['var(--bcv-ink3)', MODE_ONE, noBack ? 'One question at a time, and you cannot go back to a previous question.' : 'One question at a time.'] : null,
        attemptsLeft !== null ? ['var(--bcv-ink3)', IC.bolt, attemptsLeft > 0 ? `${U.plural(attemptsLeft, 'attempt')} left of ${quiz.allowed_attempts}.` : 'No attempts left.'] : ['var(--bcv-ink3)', IC.bolt, 'Unlimited attempts.'],
        quiz.lock_at ? ['var(--bcv-ink3)', IC.lock, `Available until ${U.fmtAt(quiz.lock_at)}.`] : null,
      ].filter(Boolean);
      const codeInput = quiz.access_code || quiz.has_access_code ? h('input', { class: 'bcv-input', type: 'text', placeholder: 'Access code', autocomplete: 'off', oninput: (e) => { st.code = e.target.value; } }) : null;
      const canStart = !quiz.locked_for_user && (attemptsLeft === null || attemptsLeft > 0 || st.sub);
      const startBtn = h('button', { type: 'button', class: 'bcv-qz__begin', text: st.sub ? 'Continue attempt' : 'Begin attempt', disabled: !canStart || null, onclick: begin });
      return U.el('bcv-qz__intro', [
        h('div', {}, [
          h('h1', { class: 'bcv-qz__h1 bcv-pretty', text: quiz.title }),
          h('p', { class: 'bcv-qz__lead', text: `${course.name} · ${U.plural(quiz.question_count || 0, 'question')} · ${pts} · ${quiz.due_at ? `Due ${U.fmtTime(quiz.due_at)}, ${U.fmtShort(quiz.due_at)}` : 'No due date'}` }),
        ]),
        U.card(U.el('bcv-qz__before', [
          U.text('bcv-qz__kicker', 'Before you start'),
          quiz.description ? BCV.screens.course.prose(quiz.description, { cls: 'bcv-qz__desc' }) : h('p', { class: 'bcv-qz__p', text: 'Read each question carefully. Your answers are sent to Canvas as you pick them.' }),
          U.el('bcv-qz__bullets', bullets.map(([color, icon, text]) => U.el('bcv-qz__bullet', [U.svg(icon, { size: 18, stroke: color, width: 2, style: { flex: 'none', marginTop: '1px' } }), h('span', { class: 'bcv-pretty', text })]))),
          codeInput,
          quiz.locked_for_user ? U.text('bcv-error', quiz.lock_explanation ? htmlToText(quiz.lock_explanation, 200) : 'This quiz is locked.') : null,
        ]), 'bcv-card--22'),
        startBtn,
        h('p', { class: 'bcv-qz__note', text: st.sub ? `Started ${U.fmtTime(st.sub.started_at)} · attempt ${st.sub.attempt}` : (attemptsLeft !== null ? `Attempt ${quiz.allowed_attempts - attemptsLeft + 1} of ${quiz.allowed_attempts}` : 'Take your time — nothing is submitted until you say so.') }),
      ]);
    }

    async function begin() {
      body.replaceChildren(U.loading(st.sub ? 'Resuming your attempt…' : 'Starting your attempt…'));
      try {
        st.sub = await store.quizApi.start(cid, qid, st.code);
        st.questions = await store.quizApi.questions(st.sub);
        // Question ids arrive as strings (our Accept header); Canvas wants numeric answer ids back.
        for (const q of st.questions) q.flagged = !!q.flagged;
        st.idx = noBack ? Math.max(0, st.questions.findIndex((q) => !isAnswered(q))) : 0;
        if (st.idx < 0) st.idx = 0;
        st.stage = 'take';
        tick();
        draw();
        window.scrollTo(0, 0);
      } catch (e) {
        st.stage = 'intro';
        draw();
        U.toast(`Could not start the attempt: ${e.message}`, { error: true });
      }
    }

    function questionBlock(q, k, { compact = false } = {}) {
      const flagBtn = h('button', { type: 'button', class: `bcv-qz__flag ${q.flagged ? 'is-on' : ''}`, onclick: () => toggleFlag(q) }, [U.svg(FLAG, { size: compact ? 12 : 13, width: 2 }), h('span', { text: q.flagged ? (compact ? 'Flagged' : 'Flagged for review') : (compact ? 'Flag' : 'Flag for review') })]);
      const pts = q.points_possible !== undefined && q.points_possible !== null ? `${store.fmtPts(q.points_possible)} ${Number(q.points_possible) === 1 ? 'point' : 'points'}` : '';
      const headRow = U.el('bcv-qz__qhead', [
        h('span', { class: 'bcv-qz__qnum', text: `Question ${k + 1}` }),
        h('span', { class: 'bcv-qz__qof', text: `of ${st.questions.length}${pts ? ` · ${pts}` : ''}` }),
        INFO.has(q.question_type) ? null : flagBtn,
      ]);
      const text = BCV.screens.course.prose(q.question_text || q.question_name || '', { cls: `bcv-qz__qtext ${compact ? 'bcv-qz__qtext--compact' : ''}` });
      return h('div', { id: `bcv-q${k}`, class: 'bcv-qz__q' }, [headRow, text, answerArea(q, compact)]);
    }

    function answerArea(q, compact) {
      const type = q.question_type;
      const opts = (q.answers || []);
      if (CHOICE.has(type) || MULTI.has(type)) {
        const multi = MULTI.has(type);
        const selected = multi ? new Set((Array.isArray(q.answer) ? q.answer : []).map(String)) : new Set(q.answer !== null && q.answer !== undefined ? [String(q.answer)] : []);
        return U.el('bcv-qz__opts', opts.map((a, j) => {
          const sel = selected.has(String(a.id));
          const label = a.html ? BCV.screens.course.prose(a.html, { cls: 'bcv-qz__optlabel' }) : h('span', { class: 'bcv-qz__optlabel', text: a.text || `Option ${LETTERS[j]}` });
          return h('button', { type: 'button', class: `bcv-qz__opt ${sel ? 'is-selected' : ''} ${compact ? 'bcv-qz__opt--compact' : ''}`, 'aria-pressed': sel ? 'true' : 'false', onclick: () => {
            if (multi) {
              const next = new Set(selected);
              if (next.has(String(a.id))) next.delete(String(a.id)); else next.add(String(a.id));
              save(q, [...next].map(Number));
            } else save(q, Number(a.id));
            draw();
          } }, [
            h('span', { class: 'bcv-qz__letter', text: LETTERS[j] || String(j + 1) }),
            label,
            U.svg(CHECK, { size: compact ? 18 : 19, stroke: '#0a84ff', width: 2.6, cls: 'bcv-qz__tick' }),
          ]);
        }));
      }
      if (TEXT.has(type)) {
        const isEssay = type === 'essay_question';
        const field = isEssay
          ? h('textarea', { class: 'bcv-textarea', rows: 6, placeholder: 'Your answer…', oninput: (e) => saveText(q, e.target.value) })
          : h('input', { class: 'bcv-input', type: type === 'numerical_question' ? 'number' : 'text', step: 'any', placeholder: type === 'numerical_question' ? 'Number' : 'Your answer', oninput: (e) => saveText(q, type === 'numerical_question' && e.target.value !== '' ? Number(e.target.value) : e.target.value) });
        field.value = q.answer === null || q.answer === undefined ? '' : String(q.answer);
        return U.el('bcv-qz__text', field);
      }
      if (INFO.has(type)) return null;
      // Matching, fill-in-the-blanks, dropdowns, file upload, calculated…: Canvas's own page handles these on the same attempt.
      return U.card(U.el('bcv-detail', [
        U.text('bcv-hint', `This ${type.replace(/_/g, ' ').replace(' question', '')} question is answered on Canvas's quiz page. Your other answers are already saved there.`),
        U.btn('Answer in Canvas', { kind: 'primary', icon: IC.external, iconColor: '#fff', onClick: () => { setOpen(false); app.go(`${quizUrl}/take?bcv=native`, { confirmed: true }); } }),
      ]), 'bcv-card--22');
    }

    let footerEl = null;
    function paintFooter() {
      if (!footerEl) return;
      const lbl = footerEl.querySelector('.bcv-qz__answered');
      if (lbl) lbl.textContent = `${answeredLabel()}${saveState() ? ` · ${saveState()}` : ''}`;
    }
    function footer(buttons) {
      footerEl = U.el('bcv-qz__foot', U.el('bcv-qz__footin', [h('span', { class: 'bcv-qz__answered' }), ...buttons]));
      paintFooter();
      return footerEl;
    }

    function takeOne() {
      const q = cur();
      const k = st.idx;
      if (!q) return U.el('bcv-qz__page', U.emptyCard('This quiz has no questions.'));
      const last = k === st.questions.length - 1;
      return h('div', { class: 'bcv-qz__stage' }, [
        U.el('bcv-qz__page', questionBlock(q, k)),
        footer([
          noBack ? null : h('button', { type: 'button', class: 'bcv-qz__btn', text: 'Back', disabled: k === 0 || null, onclick: () => { st.idx = Math.max(0, k - 1); draw(); window.scrollTo(0, 0); } }),
          last
            ? h('button', { type: 'button', class: 'bcv-qz__btn bcv-qz__btn--primary', text: 'Review answers', onclick: () => { st.stage = 'review'; draw(); window.scrollTo(0, 0); } })
            : h('button', { type: 'button', class: 'bcv-qz__btn bcv-qz__btn--primary bcv-qz__btn--next', text: 'Next', onclick: () => { st.idx = Math.min(st.questions.length - 1, k + 1); draw(); window.scrollTo(0, 0); } }),
        ]),
      ]);
    }

    function takeAll() {
      return h('div', { class: 'bcv-qz__stage' }, [
        U.el('bcv-qz__page bcv-qz__page--all', st.questions.map((q, k) => questionBlock(q, k, { compact: true }))),
        footer([h('button', { type: 'button', class: 'bcv-qz__btn bcv-qz__btn--primary', text: 'Review answers', onclick: () => { st.stage = 'review'; draw(); window.scrollTo(0, 0); } })]),
      ]);
    }

    function answerText(q) {
      const a = q.answer;
      if (!answered(a)) return null;
      const opts = q.answers || [];
      const name = (id) => { const o = opts.find((x) => String(x.id) === String(id)); return o ? (o.text || htmlToText(o.html || '', 80)) : String(id); };
      if (Array.isArray(a)) return a.map(name).join(', ');
      if (CHOICE.has(q.question_type)) return name(a);
      return String(a);
    }

    function review() {
      const blanks = st.questions.length - answeredCount();
      return U.el('bcv-qz__review', [
        h('div', {}, [
          h('h1', { class: 'bcv-qz__h1 bcv-qz__h1--30', text: 'Review before submitting' }),
          h('p', { class: 'bcv-qz__lead', text: `${answeredLabel()} · ${blanks === 0 ? 'nothing left blank' : `${blanks} left blank`}. Tap any question to change it.` }),
        ]),
        U.card(st.questions.map((q, k) => {
          const txt = answerText(q);
          return h('button', { type: 'button', class: 'bcv-qz__sum', disabled: noBack || null, onclick: () => {
            if (noBack) return;
            st.stage = 'take';
            st.idx = k;
            draw();
            if (st.mode === 'all') document.getElementById(`bcv-q${k}`)?.scrollIntoView({ block: 'start' });
            else window.scrollTo(0, 0);
          } }, [
            h('span', { class: 'bcv-qz__sumn', text: `Q${k + 1}` }),
            h('span', { class: 'bcv-qz__sumq bcv-ellip', text: htmlToText(q.question_text || q.question_name || '', 120).replace(/\s+/g, ' ') }),
            q.flagged ? U.svg(FLAG, { size: 13, stroke: '#ff9500', width: 2, style: { flex: 'none' } }) : null,
            h('span', { class: `bcv-qz__suma ${txt === null && !INFO.has(q.question_type) ? 'is-blank' : ''}`, text: INFO.has(q.question_type) ? '—' : (txt === null ? 'Not answered' : txt) }),
          ]);
        }), 'bcv-card--list'),
        U.el('bcv-qz__reviewbtns', [
          h('button', { type: 'button', class: 'bcv-qz__big', text: 'Keep working', onclick: () => { st.stage = 'take'; draw(); window.scrollTo(0, 0); } }),
          h('button', { type: 'button', class: 'bcv-qz__big bcv-qz__big--primary', text: 'Submit quiz', onclick: submit }),
        ]),
        h('p', { class: 'bcv-qz__note bcv-pretty', text: 'Submitting ends the attempt. Blank questions are graded as incorrect.' }),
      ]);
    }

    async function submit() {
      const blanks = st.questions.length - answeredCount();
      if (!window.confirm(`Submit this attempt now?${blanks ? `\n\n${U.plural(blanks, 'question is', 'questions are')} still blank.` : ''}`)) return;
      body.replaceChildren(U.loading('Submitting…'));
      try {
        st.done = await store.quizApi.complete(cid, qid, st.sub, st.code);
        st.stage = 'done';
        clearInterval(st.timer);
        app.refreshCounts();
        draw();
        window.scrollTo(0, 0);
      } catch (e) {
        st.stage = 'review';
        draw();
        U.toast(`Could not submit: ${e.message}`, { error: true });
      }
    }

    function timeUp() {
      U.toast('Time is up. Canvas closes the attempt at the time limit.', { error: true });
      st.stage = 'review';
      draw();
    }

    function done() {
      const d = st.done || {};
      const scoreVisible = !quiz.hide_results && d.score !== null && d.score !== undefined && d.workflow_state !== 'pending_review';
      return U.el('bcv-qz__done', [
        U.el('bcv-qz__donemark', U.svg(CHECK, { size: 34, stroke: '#34c759', width: 2.4 })),
        h('div', {}, [
          h('h1', { class: 'bcv-qz__h1 bcv-qz__h1--28', text: 'Attempt submitted' }),
          h('p', { class: 'bcv-qz__lead bcv-pretty', text: `${quiz.title} · submitted ${U.fmtAtUpper(d.finished_at || new Date())}. ${scoreVisible ? '' : 'Your score posts once your instructor releases it.'}` }),
        ]),
        U.el('bcv-qz__donecard', [h('span', { class: 'bcv-qz__donek', text: 'Questions answered' }), h('span', { class: 'bcv-qz__donev', text: answeredLabel() })]),
        scoreVisible ? U.el('bcv-qz__donecard', [h('span', { class: 'bcv-qz__donek', text: 'Score' }), h('span', { class: 'bcv-qz__donev', text: `${store.fmtPts(d.kept_score ?? d.score)} / ${store.fmtPts(quiz.points_possible || 0)}` })]) : null,
        U.el('bcv-qz__donebtns', [
          h('button', { type: 'button', class: 'bcv-qz__big bcv-qz__big--primary', text: `Back to ${course.name}`, onclick: leave }),
          h('button', { type: 'button', class: 'bcv-qz__big', text: 'Quiz page', onclick: () => { setOpen(false); app.go(quizUrl, { confirmed: true }); } }),
        ]),
      ]);
    }

    draw();
    return screen;
  }

  BCV.screens.quiz = { render };
})();
