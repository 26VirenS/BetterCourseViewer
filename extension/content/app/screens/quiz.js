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
  const QP = () => BCV.quizPage; // Canvas's own quiz page as a question source (one-question-at-a-time quizzes)
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
  // Matching saves a list of pairs and the blank kinds save one value per blank, so neither an empty
  // list nor an empty set of blanks counts as answered.
  const answered = (v) => {
    if (v === null || v === undefined || v === '') return false;
    if (Array.isArray(v)) return !!v.length;
    if (typeof v === 'object') return !!Object.keys(v).length;
    return true;
  };
  const CHOICE = new Set(['multiple_choice_question', 'true_false_question']);
  const MULTI = new Set(['multiple_answers_question']);
  const TEXT = new Set(['short_answer_question', 'essay_question', 'numerical_question']);
  const MATCH = new Set(['matching_question']);
  // one field per blank: a dropdown of that blank's own list, or a line to type in
  const DROPS = new Set(['multiple_dropdowns_question']);
  const BLANKS = new Set(['multiple_dropdowns_question', 'fill_in_multiple_blanks_question']);
  const INFO = new Set(['text_only_question']);

  async function render(ctx, course) {
    const { app, route } = ctx;
    const cid = course.id;
    const qid = route.arg;
    const quizUrl = `${course.url}/quizzes/${qid}`;
    const html = document.documentElement;
    // The flow lives in the course's own column. The intro and the feedback sit there like any tab
    // (the course header and rail stay); an attempt sets html.bcv-quiz, which folds the sidebar, the
    // header and the rail away so the questions take the page. app.js clears it on the next render.

    // fbSub: the finished attempt the feedback stage shows; fbFrom: 'done' when it was opened from the receipt
    const st = { stage: 'intro', idx: 0, mode: 'one', quiz: null, sub: null, questions: [], flags: {}, saving: 0, savedAt: 0, timer: null, warned: {}, done: null, code: '', fbSub: null, fbFrom: null, fb: null, page: null, paged: false, inflight: new Set(), loadingIdx: null };
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
    // a phone shows one question per screen (the mockup); the scroll-through mode is a desktop choice
    const phone = !!BCV.phone?.active();
    st.mode = quiz.one_question_at_a_time || phone ? 'one' : (await store.pref('quizMode', 'one'));
    let forcedOne = !!quiz.one_question_at_a_time || phone;
    // A quiz set to one question at a time cannot list its questions through the API (Canvas refuses:
    // "Cannot receive one question at a time questions in the API"), but Canvas's own page shows one
    // per request and the answer, flag, clock and submit calls still work. So the attempt runs here
    // as for any other quiz, each question read from that page (BCV.quizPage) and every move made
    // the way that page makes it — which is also how Canvas enforces "no going back".
    st.paged = !!quiz.one_question_at_a_time;
    const noBack = !!quiz.cant_go_back;
    const timed = !!quiz.time_limit;
    // Attempts come from Canvas's own count on the submission (plus any extra the instructor
    // granted); allowed === null means unlimited. The store refuses to start one past the limit too.
    const limit = store.quizAttemptLimit(quiz, subs);
    const attemptsLeft = limit.left;
    const allowed = limit.allowed;
    if (route.params.get('bcv') === 'feedback') {
      st.fbSub = pickFeedbackSub(route.params.get('sub'), route.params.get('attempt'));
      st.stage = 'feedback';
    }

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
    // The way out of our quiz UI without giving anything up: Canvas's own quiz page, punched through.
    // Every answer is already with Canvas — the attempt is not restarted and nothing is re-asked.
    const toRaw = () => {
      setOpen(false); // leaving on purpose: no prompt from the unload guard
      clearInterval(st.timer);
      const attempt = st.sub && (st.stage === 'take' || st.stage === 'review');
      location.href = `${location.origin}${quizUrl}${attempt ? '/take' : ''}?bcv=native`;
    };
    const rawBtn = h('button', {
      type: 'button', class: 'bcv-qz__raw', onclick: toRaw,
      title: "Show Canvas's own quiz page. Your answers are already saved with Canvas — the attempt is not restarted and the page is not reloaded from the start.",
    }, [U.svg(IC.external, { size: 13, width: 2 }), h('span', { text: 'Canvas page' })]);
    const rawNote = U.text('bcv-qz__rawnote', 'Canvas page hands the attempt to Canvas as it stands — every answer is saved, nothing restarts.');
    const head = U.el('bcv-qz__head', [
      U.el('bcv-qz__headrow', [
        h('button', { type: 'button', class: 'bcv-qz__exit', title: 'Save and exit', 'aria-label': 'Save and exit', onclick: leave }, U.svg(IC.close, { size: 16, width: 2.2 })),
        h('div', { class: 'bcv-qz__titles' }, [U.text('bcv-qz__title bcv-ellip', quiz.title), U.text('bcv-qz__sub', `${course.name} · ${dueDay}`)]),
        modeWrap,
        rawBtn,
        U.el('bcv-qz__timer', [U.svg('M12 5a8 8 0 100 16 8 8 0 000-16zM12 9v4l3 2', { size: 13, stroke: 'var(--bcv-ink3)', width: 2 }), timerLabel]),
      ]),
      progressWrap,
      rawNote,
    ]);
    const body = h('div', { class: 'bcv-qz__body' });
    screen.replaceChildren(head, body);

    const toTop = () => window.scrollTo(0, 0);

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
    const isAnswered = (q) => INFO.has(q.question_type) || (q.loaded === false ? !!q.answered : answered(q.answer)); // not yet read from Canvas's page: what its list says
    const answeredCount = () => st.questions.filter(isAnswered).length;
    const answeredLabel = () => `${answeredCount()} of ${st.questions.length} answered`;
    const saveState = () => (st.saving > 0 ? 'Saving…' : st.savedAt ? 'Saved' : '');

    async function save(q, answer) {
      q.answer = answer;
      st.saving++;
      paintFooter();
      const p = store.quizApi.answer(st.sub, q.id, answer);
      st.inflight.add(p);
      try {
        await p;
        st.savedAt = Date.now();
      } catch (e) {
        U.toast(`Could not save that answer: ${e.message}`, { error: true });
      } finally {
        st.inflight.delete(p);
        st.saving--;
        paintFooter();
        paintProgress();
      rawNote.hidden = !(st.stage === 'take' || st.stage === 'review');
      }
    }
    // typing into a blank saves on a pause, the way every other typed answer does
    let blankTimer = null;
    const saveSoon = (fn) => { clearTimeout(blankTimer); blankTimer = setTimeout(fn, 600); };
    const textTimers = new Map();
    const textPending = new Map();
    function saveText(q, value) {
      q.answer = value;
      clearTimeout(textTimers.get(q.id));
      textPending.set(q.id, [q, value]);
      textTimers.set(q.id, setTimeout(() => { textPending.delete(q.id); save(q, value); }, 600));
    }
    /** Every answer on Canvas: typed text still waiting on its pause is sent now, and the saves in flight are awaited. */
    async function settled() {
      for (const [id, [q, value]] of [...textPending]) {
        clearTimeout(textTimers.get(id));
        textPending.delete(id);
        save(q, value);
      }
      if (st.inflight.size) await Promise.allSettled([...st.inflight]);
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
      const embedded = st.stage === 'intro' || st.stage === 'feedback'; // in the column, under the course header
      modeWrap.replaceChildren(...(st.stage === 'take' && !forcedOne ? [
        modeBtn('one', 'One question at a time', MODE_ONE),
        modeBtn('all', 'Scroll through all questions', MODE_ALL),
      ] : []));
      modeWrap.hidden = !(st.stage === 'take' && !forcedOne);
      paintProgress();
      body.classList.toggle('is-busy', st.loadingIdx !== null && st.stage === 'take');
      if (st.stage === 'intro') body.replaceChildren(intro());
      else if (st.stage === 'starting') body.replaceChildren(h('p', { class: 'bcv-qz__starting', text: st.sub ? 'Resuming your attempt…' : 'Starting your attempt…' }));
      else if (st.stage === 'take') body.replaceChildren(st.mode === 'all' ? takeAll() : takeOne());
      else if (st.stage === 'review') body.replaceChildren(review());
      else if (st.stage === 'feedback') body.replaceChildren(feedback());
      else body.replaceChildren(done());
      screen.classList.toggle('is-feedback', st.stage === 'feedback');
      screen.classList.toggle('is-embedded', embedded);
      html.classList.toggle('bcv-quiz', !embedded); // the attempt (and its receipt) takes the page
      setOpen(st.stage === 'take' || st.stage === 'review');
    }
    function modeBtn(key, label, icon) {
      return h('button', { type: 'button', class: `bcv-qz__mode ${st.mode === key ? 'is-active' : ''}`, title: label, 'aria-label': label, onclick: () => { st.mode = key; store.setPref('quizMode', key); draw(); } }, U.svg(icon, { size: 15, width: 1.9 }));
    }

    // The pills are the progress bar (mockup 14): the pill of a question on its way fills left to right
    // like a sidebar row, while the question on screen stays put — no skeleton. Before the questions
    // arrive, one pill stands for each question the quiz says it has, the first of them filling.
    function paintProgress() {
      const starting = st.stage === 'starting';
      if (st.stage !== 'take' && !starting) {
        progressWrap.replaceChildren();
        return;
      }
      const all = st.mode === 'all' && !starting;
      const list = st.questions.length ? st.questions : Array.from({ length: Math.max(1, Number(quiz.question_count) || 1) }, (_, k) => ({ id: `pending-${k}`, pending: true, flagged: false, answer: null }));
      progressWrap.replaceChildren(U.el(`bcv-qz__progress ${all ? 'bcv-qz__progress--all' : ''}`, list.map((q, k) => {
        const current = !starting && k === st.idx;
        const locked = !starting && noBack && k < st.idx;
        const loading = st.loadingIdx === k;
        return h('button', {
          type: 'button',
          class: `bcv-qz__pill ${current ? 'is-current' : ''} ${!q.pending && isAnswered(q) ? 'is-answered' : ''} ${q.flagged ? 'is-flagged' : ''} ${loading ? 'is-loading' : ''}`,
          disabled: locked || null,
          title: `Question ${k + 1}${q.flagged ? ' · flagged' : ''}`,
          onclick: () => {
            if (locked || q.pending || st.loadingIdx !== null) return;
            if (st.paged) { if (k !== st.idx) turnPage({ questionId: q.id }); return; }
            st.idx = k;
            if (all) {
              document.getElementById(`bcv-q${k}`)?.scrollIntoView({ block: 'start', behavior: 'smooth' });
              paintProgress();
            } else draw();
          },
        }, h('span', { class: 'bcv-qz__pilln', text: String(k + 1) }));
      })));
    }

    function intro() {
      const pts = quiz.points_possible !== null && quiz.points_possible !== undefined ? `${store.fmtPts(quiz.points_possible)} points` : 'ungraded';
      const bullets = [
        ['#34c759', CHECK, 'Answers save as you pick them. You can leave and come back.'],
        timed ? ['#ff9500', IC.warn, `Time limit: ${quiz.time_limit} minutes. The clock starts when you begin and keeps running if you leave.`] : null,
        forcedOne ? ['var(--bcv-ink3)', MODE_ONE, noBack ? 'One question at a time, and you cannot go back to a previous question.' : 'One question at a time.'] : null,
        attemptsLeft !== null ? ['var(--bcv-ink3)', IC.bolt, attemptsLeft > 0 ? `${U.plural(attemptsLeft, 'attempt')} left of ${allowed}.` : `No attempts left — this quiz allows ${U.plural(allowed, 'attempt')}.`] : ['var(--bcv-ink3)', IC.bolt, 'Unlimited attempts.'],
        quiz.lock_at ? ['var(--bcv-ink3)', IC.lock, `Available until ${U.fmtAt(quiz.lock_at)}.`] : null,
      ].filter(Boolean);
      const codeInput = quiz.access_code || quiz.has_access_code ? h('input', { class: 'bcv-input', type: 'text', placeholder: 'Access code', autocomplete: 'off', oninput: (e) => { st.code = e.target.value; } }) : null;
      const canStart = !quiz.locked_for_user && (attemptsLeft === null || attemptsLeft > 0 || !!st.sub);
      const lastDone = canStart ? null : latestFinished();
      // out of attempts: the primary action becomes the feedback for the last one (when released)
      const startBtn = !canStart && lastDone && !resultsHidden(lastDone)
        ? h('button', { type: 'button', class: 'bcv-qz__begin', text: 'See your feedback', onclick: () => openFeedback(lastDone, 'intro') })
        : h('button', { type: 'button', class: 'bcv-qz__begin', text: st.sub ? 'Continue attempt' : (canStart ? 'Begin attempt' : 'No attempts left'), disabled: !canStart || null, onclick: begin });
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
        h('p', { class: 'bcv-qz__note', text: st.sub ? `Started ${U.fmtTime(st.sub.started_at)} · attempt ${st.sub.attempt}` : (attemptsLeft !== null ? (attemptsLeft > 0 ? `Attempt ${limit.used + 1} of ${allowed}` : `${U.plural(limit.used, 'attempt')} used of ${allowed}`) : 'Take your time — nothing is submitted until you say so.') }),
      ]);
    }

    async function begin() {
      // the popup opens at once, its pills standing for the questions to come, the first filling while they load
      st.stage = 'starting';
      st.loadingIdx = 0;
      draw();
      try {
        st.sub = await store.quizApi.start(cid, qid, st.code);
        if (!st.paged) {
          try {
            st.questions = await store.quizApi.questions(st.sub);
          } catch (e) {
            if (!/one question at a time/i.test(e.message || '')) throw e;
            st.paged = true; // the quiz did not say so, but Canvas did
            forcedOne = true;
            st.mode = 'one';
          }
        }
        if (st.paged) applyPage(await QP().fetchPage(quizUrl, { accessCode: st.code }));
        // Question ids arrive as strings (our Accept header); Canvas wants numeric answer ids back.
        for (const q of st.questions) q.flagged = !!q.flagged;
        if (!st.paged) st.idx = noBack ? Math.max(0, st.questions.findIndex((q) => !isAnswered(q))) : 0;
        if (st.idx < 0) st.idx = 0;
        st.loadingIdx = null;
        st.stage = 'take';
        tick();
        draw();
        toTop();
      } catch (e) {
        st.loadingIdx = null;
        st.stage = 'intro';
        draw();
        U.toast(`Could not start the attempt: ${e.message}`, { error: true });
      }
    }
    /** A page of Canvas's quiz page folded into the attempt: its question list is the spine (every
     *  question in order, with what Canvas knows of each) and the question shown is filled in; the
     *  others keep what was read of them before, or stay stubs until their turn. */
    function applyPage(pg) {
      if (!pg.ok || !pg.questions.length) throw QP().notShown(pg);
      st.page = pg;
      const known = new Map(st.questions.map((q) => [String(q.id), q]));
      const shown = new Map(pg.questions.map((q) => [String(q.id), q]));
      const spine = pg.list.length ? pg.list : pg.questions.map((q) => ({ id: q.id, name: q.question_name, answered: answered(q.answer), flagged: q.flagged, textOnly: q.question_type === 'text_only_question' }));
      st.questions = spine.map((e, k) => {
        const full = shown.get(String(e.id));
        if (full) return { ...full, position: k + 1 };
        const old = known.get(String(e.id));
        if (old && old.loaded !== false) return { ...old, position: k + 1, flagged: !!e.flagged };
        return { id: String(e.id), position: k + 1, question_name: e.name, question_type: e.textOnly ? 'text_only_question' : 'unknown_question', question_text: '', answers: [], answer: null, answered: !!e.answered, flagged: !!e.flagged, loaded: false };
      });
      const at = st.questions.findIndex((q) => String(q.id) === String(pg.questions[0].id));
      st.idx = at >= 0 ? at : 0;
    }
    /** A move on a paged attempt goes through Canvas's own page: Next and Previous post its record-answer
     *  form (the question is marked read, which is what "no going back" rests on); a pill fetches that
     *  question's page. Every answer is on Canvas before the move, since a read question takes no more. */
    async function turnPage(how) {
      if (st.loadingIdx !== null) return; // one move at a time
      // the pill of the question on its way is the progress bar; the question on screen stays, inert, until it arrives
      const target = how.questionId !== undefined ? st.questions.findIndex((q) => String(q.id) === String(how.questionId)) : st.idx + (how.toward || 1);
      st.loadingIdx = Math.max(0, Math.min(st.questions.length - 1, target));
      paintProgress();
      body.classList.add('is-busy');
      await settled();
      if (!ctx.alive()) return;
      try {
        const f = st.page?.form || {};
        const fields = { attempt: f.attempt ?? st.sub.attempt, validation_token: f.validationToken || st.sub.validation_token, last_question_id: f.lastQuestionId || cur()?.id || null };
        const pg = how.action ? await QP().advance(how.action, fields) : await QP().fetchPage(quizUrl, { questionId: how.questionId, accessCode: st.code });
        if (!ctx.alive()) return;
        st.loadingIdx = null;
        applyPage(pg);
        draw();
        toTop();
      } catch (e) {
        if (!ctx.alive()) return;
        st.loadingIdx = null;
        draw();
        U.toast(`Could not move to that question: ${e.message}`, { error: true });
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
        const chosen = () => (multi
          ? new Set((Array.isArray(q.answer) ? q.answer : []).map(String))
          : new Set(q.answer !== null && q.answer !== undefined ? [String(q.answer)] : []));
        // A pick marks the options in place rather than drawing the question again: a question whose
        // text or options hold a formula would otherwise rebuild every equation image Canvas serves,
        // and the formulas visibly blink away and back on each press. save() repaints the count and
        // the pills itself, so nothing else here has to move.
        const btns = [];
        const marks = () => {
          const on = chosen();
          for (const [a, b] of btns) {
            const sel = on.has(String(a.id));
            b.classList.toggle('is-selected', sel);
            b.setAttribute('aria-pressed', sel ? 'true' : 'false');
          }
        };
        const wrap = U.el('bcv-qz__opts', opts.map((a, j) => {
          const label = a.html ? BCV.screens.course.prose(a.html, { cls: 'bcv-qz__optlabel' }) : h('span', { class: 'bcv-qz__optlabel', text: a.text || `Option ${LETTERS[j]}` });
          const b = h('button', { type: 'button', class: `bcv-qz__opt ${compact ? 'bcv-qz__opt--compact' : ''}`, onclick: () => {
            if (multi) {
              const next = chosen();
              if (next.has(String(a.id))) next.delete(String(a.id)); else next.add(String(a.id));
              save(q, [...next].map(Number));
            } else save(q, Number(a.id));
            marks();
          } }, [
            h('span', { class: 'bcv-qz__letter', text: LETTERS[j] || String(j + 1) }),
            label,
            U.svg(CHECK, { size: compact ? 18 : 19, stroke: '#0a84ff', width: 2.6, cls: 'bcv-qz__tick' }),
          ]);
          btns.push([a, b]);
          return b;
        }));
        marks();
        return wrap;
      }
      if (TEXT.has(type)) {
        const isEssay = type === 'essay_question';
        const field = isEssay
          ? h('textarea', { class: 'bcv-textarea', rows: 6, placeholder: 'Your answer…', oninput: (e) => saveText(q, e.target.value) })
          : h('input', { class: 'bcv-input', type: type === 'numerical_question' ? 'number' : 'text', step: 'any', placeholder: type === 'numerical_question' ? 'Number' : 'Your answer', oninput: (e) => saveText(q, type === 'numerical_question' && e.target.value !== '' ? Number(e.target.value) : e.target.value) });
        field.value = q.answer === null || q.answer === undefined ? '' : String(q.answer);
        return U.el('bcv-qz__text', field);
      }
      // Matching: each left-hand value with the same list of right-hand ones beside it. Canvas takes
      // the picks as pairs — the answer's own id against the match it was set to.
      if (MATCH.has(type)) {
        const matches = q.matches || [];
        const chosen = new Map((Array.isArray(q.answer) ? q.answer : []).map((p2) => [String(p2.answer_id), String(p2.match_id)]));
        const rows = [];
        const send = () => {
          const pairs = [];
          for (const [aid, sel] of rows) { const v = sel.bcvPicker ? sel.bcvPicker.value : sel.value; if (v) pairs.push({ answer_id: Number(aid) || aid, match_id: Number(v) || v }); }
          save(q, pairs);
        };
        return U.el(`bcv-qz__match ${compact ? 'bcv-qz__match--compact' : ''}`, opts.map((a) => {
          const sel = U.picker(
            [{ value: '', text: 'Choose…' }, ...matches.map((m) => ({ value: String(m.match_id), text: m.text, html: m.html || '' }))],
            chosen.get(String(a.id)) || '', send, { label: `Match for ${a.text || a.left || 'this'}`, cls: 'bcv-qz__sel' },
          );
          rows.push([String(a.id), sel]);
          const left = a.html ? BCV.screens.course.prose(a.html, { cls: 'bcv-qz__matchleft' }) : h('span', { class: 'bcv-qz__matchleft', text: a.text || a.left || '' });
          return U.el('bcv-qz__matchrow', [left, sel]);
        }));
      }
      // A blank each: a dropdown of that blank's own list, or a line to type in. Canvas takes them as
      // one value per blank, named for the blank.
      if (BLANKS.has(type)) {
        const drops = DROPS.has(type);
        const blanks = q.blanks?.length ? q.blanks : [...new Set(opts.map((a) => a.blank_id).filter(Boolean))];
        const held = q.answer && typeof q.answer === 'object' && !Array.isArray(q.answer) ? q.answer : {};
        const fields = [];
        const send = () => {
          const out = {};
          for (const [blank, f] of fields) {
            const v = String((f.bcvPicker ? f.bcvPicker.value : f.value) || '').trim();
            if (v) out[blank] = drops ? (Number(v) || v) : v;
          }
          save(q, out);
        };
        if (!blanks.length) return null;
        return U.el(`bcv-qz__blanks ${compact ? 'bcv-qz__blanks--compact' : ''}`, blanks.map((blank) => {
          const mine = opts.filter((a) => String(a.blank_id) === String(blank));
          const held0 = held[blank] === undefined || held[blank] === null ? '' : String(held[blank]);
          const f = drops
            ? U.picker(
              [{ value: '', text: 'Choose…' }, ...mine.map((a) => ({ value: String(a.id), text: a.text || '', html: a.html || '' }))],
              held0, send, { label: blank, cls: 'bcv-qz__sel' },
            )
            : h('input', { class: 'bcv-input', type: 'text', placeholder: 'Your answer', 'aria-label': blank, oninput: () => saveSoon(send) });
          if (!drops) f.value = held0;
          fields.push([blank, f]);
          return U.el('bcv-qz__blankrow', [U.text('bcv-qz__blanklbl', blank, 'span'), f]);
        }));
      }
      if (INFO.has(type)) return null;
      // Matching, fill-in-the-blanks, dropdowns, file upload, calculated…: Canvas's own page handles these on the same attempt.
      return U.card(U.el('bcv-detail', [
        U.text('bcv-hint', `This ${type.replace(/_/g, ' ').replace(' question', '')} question is answered on Canvas's quiz page. Your other answers are already saved there.`),
        U.btn('Answer in Canvas', { kind: 'primary', icon: IC.external, iconColor: '#fff', onClick: () => { setOpen(false); app.go(st.paged && !noBack ? `${quizUrl}/take/questions/${q.id}?bcv=native` : `${quizUrl}/take?bcv=native`, { confirmed: true }); } }),
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
      const last = st.paged ? !st.page?.form.nextAction : k === st.questions.length - 1;
      const back = () => {
        if (st.paged) { turnPage(st.page?.form.prevAction ? { action: st.page.form.prevAction, toward: -1 } : { questionId: st.questions[k - 1].id }); return; }
        st.idx = Math.max(0, k - 1);
        draw();
        toTop();
      };
      const next = () => {
        if (st.paged) { turnPage({ action: st.page.form.nextAction, toward: 1 }); return; }
        st.idx = Math.min(st.questions.length - 1, k + 1);
        draw();
        toTop();
      };
      return h('div', { class: 'bcv-qz__stage' }, [
        U.el('bcv-qz__page', questionBlock(q, k)),
        footer([
          noBack ? null : h('button', { type: 'button', class: 'bcv-qz__btn', text: 'Back', disabled: k === 0 || null, onclick: back }),
          last
            ? h('button', { type: 'button', class: 'bcv-qz__btn bcv-qz__btn--primary', text: 'Review answers', onclick: () => { st.stage = 'review'; draw(); toTop(); } })
            : h('button', { type: 'button', class: 'bcv-qz__btn bcv-qz__btn--primary bcv-qz__btn--next', text: 'Next', onclick: next }),
        ]),
      ]);
    }

    function takeAll() {
      return h('div', { class: 'bcv-qz__stage' }, [
        U.el('bcv-qz__page bcv-qz__page--all', st.questions.map((q, k) => questionBlock(q, k, { compact: true }))),
        footer([h('button', { type: 'button', class: 'bcv-qz__btn bcv-qz__btn--primary', text: 'Review answers', onclick: () => { st.stage = 'review'; draw(); toTop(); } })]),
      ]);
    }

    /** An answer as pieces to show: its own words, and the rich content Canvas holds it in when there
     *  is any — which is how a formula arrives, as an equation image with the LaTeX on the tag. */
    const partText = (p) => (String(p.text).trim() ? p.text : htmlToText(p.html || '', 80)) || '—';
    function answerParts(q) {
      const a = q.answer;
      if (!answered(a)) return null;
      const opts = q.answers || [];
      const one = (id) => {
        const o = opts.find((x) => String(x.id) === String(id));
        return o ? { text: o.text || '', html: o.html || '' } : { text: String(id), html: '' };
      };
      // a pair each: the left-hand value and what it was set to
      if (MATCH.has(q.question_type)) {
        const nameOf = (mid) => (q.matches || []).find((m) => String(m.match_id) === String(mid))?.text || String(mid);
        return a.map((p2) => ({ text: `${one(p2.answer_id).text || partText(one(p2.answer_id))} → ${nameOf(p2.match_id)}`, html: '' }));
      }
      // one value per blank, named for the blank it fills
      if (BLANKS.has(q.question_type)) {
        return Object.entries(a).map(([blank, v]) => {
          const o = DROPS.has(q.question_type) ? opts.find((x) => String(x.id) === String(v) && String(x.blank_id) === String(blank)) : null;
          return { text: `${blank}: ${o ? (o.text || htmlToText(o.html || '', 60)) : v}`, html: '' };
        });
      }
      if (Array.isArray(a)) return a.map(one);
      if (CHOICE.has(q.question_type)) return [one(a)];
      return [{ text: String(a), html: '' }];
    }
    /** The same, flattened to one line — for the review list and anywhere a plain string is wanted. */
    function answerText(q) {
      const parts = answerParts(q);
      return parts ? parts.map(partText).join(', ') : null;
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
            if (st.paged) { turnPage({ questionId: q.id }); return; }
            st.idx = k;
            draw();
            if (st.mode === 'all') document.getElementById(`bcv-q${k}`)?.scrollIntoView({ block: 'start' });
            else toTop();
          } }, [
            h('span', { class: 'bcv-qz__sumn', text: `Q${k + 1}` }),
            h('span', { class: 'bcv-qz__sumq bcv-ellip', text: htmlToText(q.question_text || q.question_name || '', 120).replace(/\s+/g, ' ') }),
            q.flagged ? U.svg(FLAG, { size: 13, stroke: '#ff9500', width: 2, style: { flex: 'none' } }) : null,
            h('span', { class: `bcv-qz__suma ${txt === null && !INFO.has(q.question_type) && !isAnswered(q) ? 'is-blank' : ''}`, text: INFO.has(q.question_type) ? '—' : (txt !== null ? txt : isAnswered(q) ? 'Answered' : 'Not answered') }),
          ]);
        }), 'bcv-card--list'),
        U.el('bcv-qz__reviewbtns', [
          h('button', { type: 'button', class: 'bcv-qz__big', text: 'Keep working', onclick: () => { st.stage = 'take'; draw(); toTop(); } }),
          h('button', { type: 'button', class: 'bcv-qz__big bcv-qz__big--primary', text: 'Submit quiz', onclick: submit }),
        ]),
        h('p', { class: 'bcv-qz__note bcv-pretty', text: 'Submitting ends the attempt. Blank questions are graded as incorrect.' }),
      ]);
    }

    async function submit() {
      const blanks = st.questions.length - answeredCount();
      if (!window.confirm(`Submit this attempt now?${blanks ? `\n\n${U.plural(blanks, 'question is', 'questions are')} still blank.` : ''}`)) return;
      body.replaceChildren(h('p', { class: 'bcv-qz__starting', text: 'Submitting…' })); // inside the attempt nothing is a skeleton
      try {
        st.done = await store.quizApi.complete(cid, qid, st.sub, st.code);
        st.stage = 'done';
        clearInterval(st.timer);
        app.refreshCounts();
        draw();
        toTop();
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
          // feedback only once Canvas has released it (hide_results); the receipt says so otherwise
          scoreVisible && d.id && !resultsHidden(d) ? h('button', { type: 'button', class: 'bcv-qz__big bcv-qz__big--primary', text: 'See feedback', onclick: () => openFeedback(d, 'done') }) : null,
          h('button', { type: 'button', class: `bcv-qz__big ${scoreVisible && d.id && !resultsHidden(d) ? '' : 'bcv-qz__big--primary'}`, text: `Back to ${course.name}`, onclick: () => exitTo(course.url) }),
          h('button', { type: 'button', class: 'bcv-qz__big', text: 'Quiz page', onclick: () => exitTo(quizUrl) }),
        ]),
      ]);
    }

    // ---- feedback (mockup 9) ---------------------------------------------------------
    // A finished attempt, question by question: your answer, the correct one when the
    // quiz's settings allow it, the instructor's worked solution. Everything comes from the attempt's own question data
    // and the assignment submission's comments; nothing is fetched beyond that.
    const CS = () => BCV.screens.course;
    /** A chip's contents: the label, then each answer — its own words where it has them, and Canvas's
     *  own content where it does not, so an answer that is a formula shows the formula. */
    function chipBody(label, parts) {
      const out = [h('span', { text: label })];
      if (!parts?.length) return [...out, h('span', { text: 'no answer' })];
      parts.forEach((p, i) => {
        if (i) out.push(h('span', { text: ', ' }));
        if (String(p.text || '').trim()) out.push(h('span', { text: p.text }));
        else if (String(p.html || '').trim()) out.push(CS().prose(p.html, { cls: 'bcv-fb__chiprich' }));
        else out.push(h('span', { text: '—' }));
      });
      return out;
    }
    const CROSS = 'M6 6l12 12M18 6L6 18';
    function finished(s) {
      return !!s && (s.workflow_state === 'complete' || s.workflow_state === 'pending_review');
    }
    function latestFinished() {
      return [...(subs || []), ...(st.done && finished(st.done) ? [st.done] : [])]
        .filter(finished)
        .sort((a, b) => (Number(b.attempt) || 0) - (Number(a.attempt) || 0))[0] || null;
    }
    function pickFeedbackSub(wantId, wantAttempt) {
      const list = (subs || []).filter(finished);
      if (wantId) {
        const m = list.find((s) => String(s.id) === String(wantId));
        if (m) return m;
      }
      const n = Number(wantAttempt);
      if (n > 0) {
        const m = list.find((s) => Number(s.attempt) === n);
        if (m) return m;
        // Canvas keeps one quiz submission per student, so an earlier attempt has none of its own:
        // it is read through that same submission, scoped to the attempt asked for, and its score
        // and grading come from the assignment submission's history.
        const any = (subs || [])[0];
        if (any) return { ...any, attempt: n, workflow_state: 'complete', finished_at: null, score: null, kept_score: null };
      }
      return latestFinished();
    }
    function exitTo(href) {
      setOpen(false);
      clearInterval(st.timer);
      app.go(href, { confirmed: true });
    }
    function openFeedback(sub, from) {
      st.fbSub = sub;
      st.fbFrom = from;
      st.fb = null;
      st.stage = 'feedback';
      draw();
      toTop();
    }
    /** Why results are withheld (a sentence), or null when Canvas shows them. */
    function resultsHidden(sub) {
      if (quiz.hide_results === 'always') return 'Your instructor has hidden the results for this quiz.';
      if (quiz.hide_results === 'until_after_last_attempt' && allowed !== null && attemptsLeft > 0) return `Results show after your last attempt — ${U.plural(attemptsLeft, 'attempt')} left.`;
      if (sub && sub.workflow_state === 'untaken') return 'This attempt is still open.';
      return null;
    }
    /** Whether the quiz's show_correct_answers window is open right now. */
    function correctVisible() {
      if (!quiz.show_correct_answers) return false;
      const now = Date.now();
      if (quiz.show_correct_answers_at && U.parse(quiz.show_correct_answers_at) > now) return false;
      if (quiz.hide_correct_answers_at && U.parse(quiz.hide_correct_answers_at) < now) return false;
      if (quiz.show_correct_answers_last_attempt && allowed !== null && attemptsLeft > 0) return false;
      return true;
    }
    const parseCorrect = (v) => (v === true || v === 'true' ? true : v === false || v === 'false' ? false : v === 'partial' ? 'partial' : null);
    /** The student's answer as the graded history records it (answer_id, answer_<id> flags, or text). */
    function histAnswer(q, d) {
      if (MULTI.has(q.question_type)) {
        const on = Object.keys(d).filter((k) => /^answer_\d+$/.test(k) && String(d[k]) === '1').map((k) => Number(k.slice(7)));
        return on.length ? on : null;
      }
      const text = d.text === undefined || d.text === null || d.text === '' ? null : d.text;
      if (CHOICE.has(q.question_type)) return d.answer_id ?? (text !== null && Number.isFinite(Number(text)) ? Number(text) : null);
      return text;
    }
    /** The correct answer(s): the answers Canvas weights 100 (absent when censored), as the pieces
     *  answerParts gives, so a formula shows as the formula rather than as nothing. */
    function fbRight(q) {
      const one = (a) => {
        if (String(a.text || '').trim() || String(a.html || '').trim()) return { text: a.text || '', html: a.html || '' };
        if (a.numerical_answer_type === 'range_answer' && a.start !== undefined) return { text: `${a.start} – ${a.end}`, html: '' };
        if (a.exact !== undefined && a.exact !== null) return { text: Number(a.margin) ? `${a.exact} ± ${a.margin}` : String(a.exact), html: '' };
        if (a.approximate !== undefined && a.approximate !== null) return { text: String(a.approximate), html: '' };
        if (a.left && a.right) return { text: `${a.left} → ${a.right}`, html: '' };
        return null;
      };
      const parts = (q.answers || []).filter((a) => Number(a.weight) === 100).map(one).filter(Boolean);
      return parts.length ? parts : null;
    }
    /** Solution html from the question data: the neutral comment, else the one for this outcome. */
    function fbSolution(q, ok) {
      const html = q.neutral_comments_html || (ok ? q.correct_comments_html : q.incorrect_comments_html);
      if (html && String(html).trim()) return { html };
      const text = q.neutral_comments || (ok ? q.correct_comments : q.incorrect_comments);
      return text && String(text).trim() ? { text: String(text).trim() } : null;
    }
    async function loadFeedback(sub) {
      const [qs, asub] = await Promise.all([
        store.quizApi.questions(sub, { courseId: cid, quizId: qid }),
        quiz.assignment_id ? store.submission(cid, quiz.assignment_id, { force: true }).catch(() => null) : Promise.resolve(null),
      ]);
      const me = String(store.env().current_user_id || '');
      const comments = (asub?.submission_comments || []).filter((c) => !me || String(c.author_id ?? '') !== me);
      // per-question points, when the assignment submission's history carries this attempt's grading
      const hist = (asub?.submission_history || []).find((x) => Number(x.attempt) === Number(sub.attempt)) || null;
      const graded = new Map((hist?.submission_data || []).map((d) => [String(d.question_id), d]));
      const rows = qs.map((q, k) => {
        const d = graded.get(String(q.id)) || null;
        if (d && !answered(q.answer)) q.answer = histAnswer(q, d); // a one-at-a-time quiz: its answers come from the graded history
        const correct = parseCorrect(q.correct) ?? (d ? parseCorrect(d.correct) : null);
        const possible = Number(q.points_possible) || 0;
        const earned = d && d.points !== undefined && d.points !== null ? Number(d.points) : correct === true ? possible : correct === false ? 0 : null;
        return { q, k, correct, possible, earned, text: htmlToText(q.question_text || q.question_name || '', 400).replace(/\s+/g, ' ').trim(), yours: answerParts(q), right: fbRight(q), sol: fbSolution(q, correct === true), info: INFO.has(q.question_type) };
      }).filter((r) => !r.info);
      const released = rows.some((r) => r.correct !== null);
      const possible = Number(quiz.points_possible) || rows.reduce((s, r) => s + r.possible, 0);
      const score = hist?.score ?? sub.score ?? sub.kept_score; // that attempt's own score, not the best so far
      return { sub, rows, released, comments, possible, score: score === null || score === undefined ? null : Number(score), gradedAt: asub?.graded_at || sub.finished_at };
    }
    /** Every option the question offered, folded away under the answer. The chips say what you put
     *  and what was right; this is for checking the rest — what the other options actually were, and
     *  which of them you picked. Nothing is revealed that the quiz keeps back: an option is marked
     *  correct only where Canvas already shows the correct answer. */
    function fbOptions(r) {
      const q = r.q;
      const opts = q.answers || [];
      if (!(CHOICE.has(q.question_type) || MULTI.has(q.question_type)) || !opts.length) return null;
      const a = q.answer;
      const mine = new Set((Array.isArray(a) ? a : answered(a) ? [a] : []).map(String));
      const marksRight = correctVisible() && r.correct !== null;
      const list = U.el('bcv-fb__opts', opts.map((o, j) => {
        const picked = mine.has(String(o.id));
        const right = marksRight && Number(o.weight) === 100;
        const body = String(o.text || '').trim()
          ? h('span', { class: 'bcv-fb__opttext', text: o.text })
          : String(o.html || '').trim()
            ? CS().prose(o.html, { cls: 'bcv-fb__opttext bcv-fb__chiprich' })
            : h('span', { class: 'bcv-fb__opttext', text: '—' });
        return U.el(`bcv-fb__opt ${picked ? 'is-mine' : ''} ${right ? 'is-right' : ''}`, [
          h('span', { class: 'bcv-fb__optletter', text: LETTERS[j] || String(j + 1) }),
          body,
          picked ? U.text('bcv-fb__opttag', 'Your answer', 'span') : null,
          right ? U.text('bcv-fb__opttag bcv-fb__opttag--right', 'Correct', 'span') : null,
        ]);
      }));
      list.hidden = true;
      const label = U.text('bcv-fb__morelbl', `Show all ${U.plural(opts.length, 'option')}`, 'span');
      const btn = h('button', {
        type: 'button', class: 'bcv-fb__more', 'aria-expanded': 'false',
        onclick: () => {
          const open = list.hidden;
          list.hidden = !open;
          btn.setAttribute('aria-expanded', open ? 'true' : 'false');
          btn.classList.toggle('is-open', open);
          label.textContent = open ? 'Hide the options' : `Show all ${U.plural(opts.length, 'option')}`;
        },
      }, [label, U.svg(IC.chevron, { size: 12, width: 2.2, cls: 'bcv-fb__morechev' })]);
      return U.el('bcv-fb__optwrap', [btn, list]);
    }

    function fbCard(r, i) {
      const dark = app.isDark();
      const ok = r.correct === true, part = r.correct === 'partial', bad = r.correct === false;
      const [ink, tint] = ok ? (dark ? ['#5ddb7d', 'rgba(52,199,89,.2)'] : ['#1e7a37', 'rgba(52,199,89,.14)'])
        : bad ? (dark ? ['#ff8098', 'rgba(255,45,85,.2)'] : ['#c01d43', 'rgba(255,45,85,.12)'])
          : part ? ['#ff9500', 'rgba(255,149,0,.16)'] : ['var(--bcv-ink3)', 'var(--bcv-fill)'];
      const mark = ok ? CHECK : bad ? CROSS : part ? 'M5 12h14' : 'M12 17h.01M9.5 9.5a2.5 2.5 0 015 0c0 1.6-2.5 2-2.5 4';
      const scoreLbl = r.earned !== null ? `${store.fmtPts(r.earned)} / ${store.fmtPts(r.possible)}` : part ? `Partial · ${store.fmtPts(r.possible)} pts` : `${store.fmtPts(r.possible)} pts`;
      const showRight = !ok && r.correct !== null && correctVisible() && !!r.right?.length;
      const sol = r.sol;
      return U.enter(U.el('bcv-fb__q', [
        U.el('bcv-fb__qhead', [
          h('span', { class: 'bcv-fb__mark', style: { background: tint } }, U.svg(mark, { size: 13, stroke: ink, width: 2.8 })),
          h('span', { class: 'bcv-fb__qn', text: `Question ${r.k + 1}` }),
          h('span', { class: 'bcv-fb__score', style: { color: ink }, text: scoreLbl }),
        ]),
        CS().prose(r.q.question_text || r.q.question_name || '', { cls: 'bcv-fb__qtext' }),
        U.el('bcv-fb__chips', [
          h('span', { class: 'bcv-fb__chip', style: { background: tint, color: ink } }, chipBody('You: ', r.yours)),
          showRight ? h('span', { class: 'bcv-fb__chip bcv-fb__chip--right' }, chipBody('Correct: ', r.right)) : null,
        ]),
        fbOptions(r),
        U.el('bcv-fb__sol', [
          U.text('bcv-fb__kicker', 'Worked solution', 'span'),
          sol ? (sol.html ? CS().prose(sol.html, { cls: 'bcv-fb__solbody' }) : h('p', { class: 'bcv-fb__solbody', text: sol.text })) : U.text('bcv-fb__none', 'Your instructor left no worked solution for this question.'),
        ]),
      ]), i, 45);
    }
    function feedbackParts(fb) {
      const { sub, rows } = fb;
      const pct = fb.possible > 0 && fb.score !== null ? Math.round((fb.score / fb.possible) * 100) : null;
      const nRight = rows.filter((r) => r.correct === true).length;
      const when = sub.workflow_state === 'pending_review' ? 'awaiting your instructor’s review' : `graded ${U.fmtAtUpper(fb.gradedAt)}`;
      const scoreCard = U.el('bcv-fb__scorecard', [
        U.el('bcv-fb__scoreline', [
          h('span', { class: 'bcv-fb__big', text: `${fb.score !== null ? store.fmtPts(fb.score) : '—'} / ${store.fmtPts(fb.possible)}` }),
          pct !== null ? h('span', { class: 'bcv-fb__pct', text: `${pct}%` }) : null,
          h('span', { class: 'bcv-fb__summary', text: `${fb.released ? `${nRight} of ${rows.length} correct · ` : ''}${when}` }),
        ]),
        U.el('bcv-fb__bar', h('div', { class: 'bcv-fb__fill', style: { width: `${pct ?? 0}%` } })),
        ...fb.comments.map((c) => U.el('bcv-fb__comment', [
          h('span', { class: 'bcv-fb__avatar', text: U.initials(c.author_name || c.author?.display_name || '') || '·' }),
          U.el('bcv-fb__cbody', [U.text('bcv-fb__ctitle', `Instructor comment${c.author_name ? ` · ${c.author_name}` : ''}`), h('p', { class: 'bcv-fb__ctext', text: c.comment || '' })]),
        ])),
      ]);
      const notice = fb.released ? null : U.hint('Canvas has not released the question results for this attempt yet — your score and the questions are shown as they stand.', 'bcv-hint--narrow');
      return [U.enter(scoreCard, 0, 45), notice, ...rows.map((r, i) => fbCard(r, i + 1))].filter(Boolean);
    }
    function feedback() {
      const sub = st.fbSub;
      const wrap = U.el('bcv-fb');
      const btns = () => U.el('bcv-fb__btns', [
        st.fbFrom === 'done' ? h('button', { type: 'button', class: 'bcv-qz__big', text: 'Back to receipt', onclick: () => { st.stage = 'done'; draw(); toTop(); } }) : null,
        st.fbFrom === 'intro' ? h('button', { type: 'button', class: 'bcv-qz__big', text: 'Quiz overview', onclick: () => { st.stage = 'intro'; draw(); toTop(); } }) : null,
        h('button', { type: 'button', class: 'bcv-qz__big bcv-qz__big--primary', text: `Back to ${course.name}`, onclick: () => exitTo(course.url) }),
      ]);
      if (!sub) {
        wrap.append(U.emptyCard('No finished attempts yet — feedback appears here once one is submitted.'), btns());
        return wrap;
      }
      const hidden = resultsHidden(sub);
      if (hidden) {
        wrap.append(U.card(U.el('bcv-detail', [h('h2', { class: 'bcv-detail__title', text: 'Results not released' }), U.text('bcv-hint', hidden)]), 'bcv-card--22'), btns());
        return wrap;
      }
      if (st.fb && st.fb.sub === sub) {
        wrap.append(...feedbackParts(st.fb), btns());
        return wrap;
      }
      wrap.append(U.loading('rows', 4));
      loadFeedback(sub).then((fb) => {
        if (!ctx.alive() || st.stage !== 'feedback' || st.fbSub !== sub) return;
        st.fb = fb;
        wrap.replaceChildren(...feedbackParts(fb), btns());
      }).catch((e) => {
        if (ctx.alive()) wrap.replaceChildren(U.errorBox(`The feedback could not be loaded: ${e.message}`), btns());
      });
      return wrap;
    }

    draw();
    return screen;
  }

  BCV.screens.quiz = { render };
})();
