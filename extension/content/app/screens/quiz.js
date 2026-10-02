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
  const MODE_SIDE = 'M3 5h18v14H3zM8 5v14M16 5v14'; // three columns: the list, the question, the controls
  // The layouts an attempt can be drawn in. 'side' (2.98.24, the default): the question in the middle
  // at Canvas's own reading size, the list of questions down the left and every control down the
  // right. 'one' and 'all' are the two that came before: one question a screen with the pills over it
  // and the buttons under it, or every question on one scroll. The choice is kept under a name of
  // its own (quizLayout), so everyone starts on the new layout once and keeps what they pick.
  const LAYOUTS = ['side', 'one', 'all'];

  // Canvas posts a grade apart from marking it, and keeps a quiz's results back until it has: its own
  // page says "Your quiz has been muted" and shows no score and no answers. The API is not so careful —
  // the questions and the grading history can still say which answers were right — so a finished
  // attempt whose assignment submission is not posted (posted_at null; absent means posted) shows
  // nothing of its grading here either.
  const heldBack = (asub) => !!asub && asub.posted_at === null && !!(asub.submitted_at || asub.workflow_state === 'graded' || asub.workflow_state === 'pending_review');
  const HELD_LINE = 'Your instructor hasn’t released the results yet. Your score and which answers were right show here once they do.';

  const pad = (n) => String(n).padStart(2, '0');
  const clock = (ms) => {
    const s = Math.max(0, Math.round(ms / 1000));
    const hh = Math.floor(s / 3600), mm = Math.floor((s % 3600) / 60), ss = s % 60;
    return hh ? `${hh}:${pad(mm)}:${pad(ss)}` : `${mm}:${pad(ss)}`;
  };
  // Matching saves a list of pairs and the blank kinds save one value per blank, so neither an empty
  // list nor an empty set of blanks counts as answered.
  // An essay is rich text the way Canvas keeps it: what its editor wrote comes as HTML (paragraphs,
  // lists), and goes back the same way. The box shows it as readable text; the review and the
  // feedback show it as formatting, never as tags.
  const looksHtml = (v) => /<\/?(p|ul|ol|li|br|div|b|strong|i|em|u|span|a|h[1-6]|blockquote|pre)\b[^>]*>/i.test(String(v || ''));
  /** Rich text as an editor would show it: a blank line between paragraphs, a bullet (or a number) per item, no tags. */
  function htmlToPlain(html) {
    const box = h('div', { html: String(html || '') });
    const out = [];
    const walk = (node, depth) => {
      for (const n of node.childNodes) {
        if (n.nodeType === 3) { out.push(n.textContent.replace(/\u00a0/g, ' ')); continue; }
        if (n.nodeType !== 1) continue;
        const tag = n.tagName.toLowerCase();
        if (tag === 'br') { out.push('\n'); continue; }
        if (tag === 'li') {
          const ordered = n.parentElement?.tagName === 'OL';
          out.push(`${'  '.repeat(Math.max(0, depth - 1))}${ordered ? `${[...n.parentElement.children].indexOf(n) + 1}. ` : '• '}`);
          walk(n, depth);
          out.push('\n');
          continue;
        }
        if (tag === 'ul' || tag === 'ol') { walk(n, depth + 1); out.push('\n'); continue; }
        if (/^(p|div|h[1-6]|blockquote|pre|tr)$/.test(tag)) { walk(n, depth); out.push('\n\n'); continue; }
        walk(n, depth);
      }
    };
    walk(box, 0);
    return out.join('').replace(/[ \t]+\n/g, '\n').replace(/\n{3,}/g, '\n\n').trim();
  }
  /** Typed text as the simple HTML Canvas keeps essays in: a paragraph per blank line, a list where every line starts with a bullet or a number. */
  function plainToHtml(text) {
    const esc = (t) => t.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    const item = /^\s*([•\-*]|\d+[.)])\s+/;
    return String(text || '').split(/\n{2,}/).map((block) => {
      const lines = block.split('\n').map((l) => l.trimEnd()).filter((l) => l.trim());
      if (!lines.length) return '';
      if (lines.every((l) => item.test(l))) {
        const ordered = lines.every((l) => /^\s*\d+[.)]\s+/.test(l));
        return `<${ordered ? 'ol' : 'ul'}>${lines.map((l) => `<li>${esc(l.replace(item, ''))}</li>`).join('')}</${ordered ? 'ol' : 'ul'}>`;
      }
      return `<p>${lines.map(esc).join('<br>')}</p>`;
    }).filter(Boolean).join('');
  }
  const answered = (v) => {
    if (v === null || v === undefined || v === '') return false;
    if (Array.isArray(v)) return !!v.length;
    if (typeof v === 'object') return !!Object.keys(v).length;
    return true;
  };
  const CHOICE = new Set(['multiple_choice_question', 'true_false_question']);
  const MULTI = new Set(['multiple_answers_question']);
  const TEXT = new Set(['short_answer_question', 'essay_question', 'numerical_question', 'calculated_question']);
  // a number to give: numerical, and formula (calculated) — its variables already put in the question for this attempt by Canvas
  const NUMERIC = new Set(['numerical_question', 'calculated_question']);
  // a file to hand in: uploaded to the student's quiz files, the answer naming it
  const FILE = new Set(['file_upload_question']);
  const MATCH = new Set(['matching_question']);
  // one field per blank: a dropdown of that blank's own list, or a line to type in
  const DROPS = new Set(['multiple_dropdowns_question']);
  const BLANKS = new Set(['multiple_dropdowns_question', 'fill_in_multiple_blanks_question']);
  /* Canvas takes an answer's id and a match's id as whole numbers and refuses anything else
   * ("match_id must be of type Integer"). A picker only ever hands back a string, so every id it
   * gives back is turned into one here. `Number(v) || v` used to do it, and let two through: an id
   * of 0, which is falsy and went as the string "0", and a missing one, which went as "undefined". */
  const whole = (v) => { const t = String(v ?? '').trim(); return /^-?\d+$/.test(t) ? Number(t) : v; };
  const hasId = (v) => v !== null && v !== undefined && String(v).trim() !== '';
  /* Canvas hands an answer back whole, not as picked (its questions endpoint deserializes "full"): a
   * matching answer lists every left-hand value, with a null match where nothing is picked yet, and a
   * blank kind lists every blank, null where it is empty. Those nulls are not picks. Read as picks
   * they were the string "null" — a row holding it, the untouched question counted as answered, and
   * every row sent up with the first real pick, which Canvas refused whole ("match_id must be of type
   * Integer"). So an answer is kept as what was picked, and nothing else. */
  const pairsOf = (v) => (Array.isArray(v) ? v : []).filter((p) => p && hasId(p.answer_id) && hasId(p.match_id));
  const filledOf = (v) => (v && typeof v === 'object' && !Array.isArray(v) ? Object.fromEntries(Object.entries(v).filter(([, x]) => hasId(x))) : {});
  /** A question with its answer as picked: the rows and blanks Canvas lists with nothing in them taken out. */
  function tidy(q) {
    if (MATCH.has(q.question_type)) { const p = pairsOf(q.answer); q.answer = p.length ? p : null; }
    else if (BLANKS.has(q.question_type) && q.answer && typeof q.answer === 'object' && !Array.isArray(q.answer)) { const f = filledOf(q.answer); q.answer = Object.keys(f).length ? f : null; }
    return q;
  }
  /** The blanks a question has, as Canvas's page names them or as its own answers say. */
  const blanksOf = (q) => (q.blanks?.length ? q.blanks : [...new Set((q.answers || []).map((a) => a.blank_id).filter(Boolean))]);
  /* The question as something to read rather than answer. Canvas's own page writes a blank's field
   * into the sentence, so the review row and the feedback would otherwise carry a live dropdown —
   * and read it as its whole list of options run together. A blank shows there as a gap instead;
   * what was picked for it is listed beside it either way. The API's text names each blank in
   * brackets instead ("Roses are [colour1]"): those are gaps too. */
  const noFields = (html, q) => {
    if (!BLANKS.has(q.question_type) || !html) return html;
    const doc = new DOMParser().parseFromString(String(html), 'text/html');
    for (const w of doc.querySelectorAll(`[name^="question_${q.id}_"], .question_input`)) w.replaceWith(doc.createTextNode('_____'));
    const named = new Set(blanksOf(q).map(String));
    const walker = doc.createTreeWalker(doc.body, NodeFilter.SHOW_TEXT);
    for (let n = walker.nextNode(); n; n = walker.nextNode()) n.textContent = n.textContent.replace(/\[([^\]\s]+)\]/g, (m, b) => (named.has(b) ? '_____' : m));
    return doc.body.innerHTML;
  };
  const INFO = new Set(['text_only_question']);

  async function render(ctx, course) {
    const { app, route } = ctx;
    const cid = course.id;
    const qid = route.arg;
    const quizUrl = `${course.url}/quizzes/${qid}`;
    // Settings → Developer → Quiz: the simulation quiz (content/app/quiz-sim.js) answers the quiz's
    // own calls in this tab — every Classic kind, marked here — and nothing goes to Canvas
    const simulated = route.params.get('sim') === '1' && String(qid) === '0';
    if (simulated && !BCV.quizSim) await BCV.lazy.load('quizsim');
    const store = simulated ? BCV.quizSim.store(BCV.store) : BCV.store;
    const simHome = `${quizUrl}?bcv=take&sim=1`;
    const html = document.documentElement;
    // The flow lives in the course's own column. The intro and the feedback sit there like any tab
    // (the course header and rail stay); an attempt sets html.bcv-quiz, which folds the sidebar, the
    // header and the rail away so the questions take the page. app.js clears it on the next render.

    // fbSub: the finished attempt the feedback stage shows; fbFrom: 'done' when it was opened from the receipt
    const st = { stage: 'intro', idx: 0, mode: 'one', quiz: null, sub: null, questions: [], flags: {}, files: {}, saving: 0, savedAt: 0, timer: null, warned: {}, done: null, code: '', fbSub: null, fbFrom: null, fb: null, page: null, paged: false, inflight: new Set(), loadingIdx: null };
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
    const [picked, asub0] = await Promise.all([
      store.pref('quizLayout', 'side'),
      quiz.assignment_id && !simulated ? store.submission(cid, quiz.assignment_id).catch(() => null) : null,
    ]);
    // a grade the instructor has not posted yet (Canvas: "Your quiz has been muted") keeps the attempt's
    // results back too — its score and which answers were right. Read here for the buttons; the
    // feedback itself reads the submission afresh before it shows a thing (loadFeedback)
    st.held = heldBack(asub0);
    const layout = LAYOUTS.includes(picked) ? picked : 'side';
    // a quiz set to one question at a time can be drawn either way that shows one question: the side
    // layout or the plain one — never all on one scroll
    st.mode = phone ? 'one' : quiz.one_question_at_a_time && layout === 'all' ? 'side' : layout;
    let forcedOne = !!quiz.one_question_at_a_time || phone;
    // A quiz set to one question at a time cannot list its questions through the API (Canvas refuses:
    // "Cannot receive one question at a time questions in the API"), but Canvas's own page shows one
    // per request and the answer, flag, clock and submit calls still work. So the attempt runs here
    // as for any other quiz, each question read from that page (BCV.quizPage) and every move made
    // the way that page makes it — which is also how Canvas enforces "no going back".
    st.paged = !!quiz.one_question_at_a_time;
    const noBack = !!quiz.cant_go_back;
    // A quiz Canvas locks seals each question the moment you leave it: an answer cannot be changed
    // and a question cannot be returned to. It is taken here like any other — noBack below is what
    // enforces the sealing — and Open in Canvas is still on the page for anyone who wants it.
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
      BCV.app?.syncQuizFlag?.(); // (the pinned tools go while the attempt is on)
    };
    const leave = () => {
      setOpen(false);
      clearInterval(st.timer);
      app.go(simulated ? (st.stage === 'intro' ? course.url : simHome) : quizUrl, { confirmed: true });
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
    // The instructions are on the intro card, but an attempt takes the page and the card with it —
    // and that is exactly when a rule about rounding or a link to a formula sheet is wanted. So they
    // stay one press away the whole way through, in a sheet over the questions, drawn as prose (the
    // quiz's own links, tables and formulas included) rather than flattened to text.
    const hasInstructions = !!htmlToText(quiz.description || '', 4000).trim();
    const instrBtn = h('button', {
      type: 'button', class: 'bcv-qz__instrbtn', onclick: openInstructions,
      title: "Show this quiz's instructions. Nothing about your attempt changes.",
    }, [U.svg(IC.book, { size: 13, width: 2 }), h('span', { text: 'Instructions' })]);
    const exitBtn = h('button', { type: 'button', class: 'bcv-qz__exit', title: 'Save and exit', 'aria-label': 'Save and exit', onclick: leave }, U.svg(IC.close, { size: 16, width: 2.2 }));
    // Developer (Settings → Developer → Quiz): the answers of an earlier attempt, filled in and saved,
    // for trying a quiz again without typing it all out. Never there unless it was turned on there.
    const devImport = await BCV.settings.get().then((s) => s?.developer?.quizImport === true).catch(() => false);
    if (!ctx.alive()) return screen;
    const devBtn = h('button', {
      type: 'button', class: 'bcv-qz__devimport', hidden: true, onclick: () => importEarlier(),
      title: 'Developer: fill in and save the answers from your last graded attempt',
    }, [U.svg('M12 4v11M7 10l5 5 5-5M5 20h14', { size: 13, width: 2 }), h('span', { text: 'Import answers' })]);
    const head = U.el('bcv-qz__head', [
      U.el('bcv-qz__headrow', [
        exitBtn,
        h('div', { class: 'bcv-qz__titles' }, [U.text('bcv-qz__title bcv-ellip', quiz.title), U.text('bcv-qz__sub', simulated ? 'Simulation · nothing goes to Canvas' : `${course.name} · ${dueDay}`)]),
        modeWrap,
        devBtn,
        instrBtn,
        rawBtn,
        U.el('bcv-qz__timer', [U.svg('M12 5a8 8 0 100 16 8 8 0 000-16zM12 9v4l3 2', { size: 13, stroke: 'var(--bcv-ink3)', width: 2 }), timerLabel]),
      ]),
      progressWrap,
    ]);
    const body = h('div', { class: 'bcv-qz__body' });
    screen.replaceChildren(head, body);

    function openInstructions() {
      const written = hasInstructions
        ? BCV.screens.course.prose(quiz.description, { cls: 'bcv-qz__desc' })
        : h('p', { class: 'bcv-qz__p', text: 'This quiz came with no instructions.' });
      const inner = U.el('bcv-qz__instr', [written]);
      if (BCV.phone?.active()) {
        BCV.phone.openSheet({ label: 'Quiz instructions', title: 'Instructions', note: quiz.title, body: inner });
        return;
      }
      document.querySelector('.bcv-sheet-ov')?.remove();
      const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': 'Quiz instructions' });
      const close = () => BCV.ui.dismiss(ov);
      ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
      ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
      ov.append(U.el('bcv-sheet bcv-sheet--instr', [
        U.el('bcv-sheet__head', [
          h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-sheet__title', 'Instructions'), U.text('bcv-sheet__desc bcv-ellip', quiz.title)]),
          h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
        ]),
        inner,
      ]));
      document.body.append(ov);
      ov.tabIndex = -1;
      ov.focus();
    }

    const toTop = () => window.scrollTo(0, 0);

    function tick() {
      if (!ctx.alive()) { // the screen was left in place (a sidebar row): the clock stops with it, and no "1 minute left" lands on the Dashboard
        clearInterval(st.timer);
        window.removeEventListener('beforeunload', onUnload);
        return;
      }
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
    // A question with more than one part is answered when every part of it is. A blank kind saves one
    // value per blank and matching saves a pair per value, so with one of three blanks filled the old
    // test — anything in the object at all — called the question answered, painted its number blue and
    // counted it in the total, while two thirds of it was still empty.
    const isAnswered = (q) => {
      if (INFO.has(q.question_type)) return true;
      if (q.loaded === false) return !!q.answered; // not yet read from Canvas's page: what its list says
      if (BLANKS.has(q.question_type)) {
        const bs = blanksOf(q);
        const held = q.answer && typeof q.answer === 'object' && !Array.isArray(q.answer) ? q.answer : {};
        return bs.length ? bs.every((b) => answered(held[b])) : answered(q.answer);
      }
      if (MATCH.has(q.question_type)) {
        const pairs = pairsOf(q.answer);
        return (q.answers || []).length ? pairs.length >= q.answers.length : !!pairs.length;
      }
      return answered(q.answer);
    };
    const answeredCount = () => st.questions.filter(isAnswered).length;
    const answeredLabel = () => `${answeredCount()} of ${st.questions.length} answered`;
    const saveState = () => (st.saving > 0 ? 'Saving…' : st.savedAt ? 'Saved' : '');

    // The access code, for the whole attempt: Canvas wants it on every answer, flag and the hand-in,
    // not only at the start. It is kept for this tab (sessionStorage, under the quiz), so an attempt
    // resumed here does not ask again; resumed in another tab, the intro asks first. Should Canvas
    // refuse a save for the code all the same (a code changed, an attempt begun on Canvas's own
    // page), the page asks for it there and then, and saves again — never a dead toast.
    const CODE_KEY = `bcv:qzcode:${qid}`;
    const remembered = () => { try { return sessionStorage.getItem(CODE_KEY) || ''; } catch { return ''; } };
    const remember = (code) => { try { if (code) sessionStorage.setItem(CODE_KEY, code); else sessionStorage.removeItem(CODE_KEY); } catch { /* fine without */ } };
    const codeFor = () => (st.code || '').trim() || remembered();
    const codeRefused = (e) => /access code/i.test(String(e?.message || ''));
    /* Canvas keeps an attempt's answers as one record, read, changed and written back whole on every
     * save (a flag too). Two saves at once each write over the other, and an answer is lost while the
     * screen shows it saved. So every write about the attempt goes up one at a time, in the order it
     * was made. */
    let writeChain = Promise.resolve();
    const inTurn = (fn) => { const run = writeChain.then(fn); writeChain = run.catch(() => {}); return run; };
    /** An answer as something to compare: ids as text, lists in any order, an empty one as nothing. */
    const canon = (v) => {
      if (v === null || v === undefined || v === '') return '';
      if (Array.isArray(v)) return JSON.stringify(v.map((x) => (x && typeof x === 'object' ? JSON.stringify(Object.entries(x).map(([k, y]) => [k, String(y)]).sort()) : String(x))).sort());
      if (typeof v === 'object') return JSON.stringify(Object.entries(v).filter(([, y]) => hasId(y)).map(([k, y]) => [k, String(y)]).sort());
      return String(v).trim();
    };
    /** The answers Canvas holds for this attempt now, by question id (as picked: tidy), or null where it cannot say. */
    async function answersOnCanvas() {
      if (st.paged) return null; // (Canvas lists no questions for a one-at-a-time quiz)
      const list = await store.quizApi.questions(st.sub).catch(() => null);
      return list ? new Map(list.map((q) => [String(q.id), tidy({ ...q }).answer])) : null;
    }
    /** The questions answered here that Canvas does not hold as answered here, sent again one at a
     *  time; what is still not there after that comes back (null: Canvas could not be asked). */
    async function mendAnswers(questions) {
      let held = await answersOnCanvas();
      if (!held) return null;
      const off = (q) => canon(held.get(String(q.id))) !== canon(q.answer);
      const want = questions.filter((q) => !INFO.has(q.question_type) && answered(q.answer));
      const again = want.filter(off);
      if (!again.length) return [];
      for (const q of again) await inTurn(() => store.quizApi.answer(st.sub, q.id, q.answer, codeFor())).catch(() => {});
      held = await answersOnCanvas();
      if (!held) return null;
      return want.filter((q) => !answered(held.get(String(q.id)))); // (still missing altogether: a value Canvas writes its own way is not a loss)
    }
    let codePrompt = null; // the prompt on show, and the saves waiting on it: [q, answer][]
    function askForCode(q, answer) {
      if (codePrompt) { codePrompt.push([q, answer]); return; }
      codePrompt = [[q, answer]];
      U.promptSheet({
        label: 'Access code', title: 'Access code', note: 'Canvas wants the quiz’s access code to save your answers. Enter it to keep going — it stays with this attempt.',
        placeholder: 'Access code', value: '', saveLabel: 'Save answer',
        onSave: async (v) => {
          if (!v) throw new Error('Enter the access code.');
          await inTurn(() => store.quizApi.answer(st.sub, q.id, answer, v)); // (refused again: the prompt says so and stays)
          st.code = v;
          remember(v);
          st.savedAt = Date.now();
          const rest = codePrompt.slice(1);
          codePrompt = null;
          paintFooter();
          for (const [q2, a2] of rest) save(q2, a2); // the answers that waited on the code
        },
      });
      // (a prompt closed without a code: the answers stay unsaved, the footer says so, and the next save asks again)
      const watch = setInterval(() => { if (!document.querySelector('.bcv-sheet--prompt')) { clearInterval(watch); codePrompt = null; } }, 400);
    }
    async function save(q, answer) {
      q.answer = answer;
      st.saving++;
      paintFooter();
      const p = inTurn(() => store.quizApi.answer(st.sub, q.id, answer, codeFor()));
      st.inflight.add(p);
      try {
        await p;
        st.savedAt = Date.now();
      } catch (e) {
        if (codeRefused(e)) askForCode(q, answer);
        else U.toast(`Could not save that answer: ${e.message}`, { error: true });
      } finally {
        st.inflight.delete(p);
        st.saving--;
        paintFooter();
        paintProgress();
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
    /** Developer: the answers of the latest earlier attempt with any, read from the graded history as
     *  the feedback reads them, put on this attempt's questions and saved to Canvas in one request —
     *  then read back from Canvas, what it did not keep sent again, and the count said is what Canvas holds. */
    async function importEarlier() {
      if (!quiz.assignment_id) { U.toast('This quiz keeps no graded history to import from.', { error: true }); return; }
      devBtn.disabled = true;
      try {
        const asub = await store.submission(cid, quiz.assignment_id, { force: true });
        if (!ctx.alive()) return;
        const earlier = (asub?.submission_history || [])
          .filter((x) => Array.isArray(x.submission_data) && x.submission_data.length && Number(x.attempt) !== Number(st.sub?.attempt))
          .sort((a, b) => Number(b.attempt) - Number(a.attempt))[0];
        if (!earlier) { U.toast('No earlier attempt with answers to import.', { error: true }); return; }
        const by = new Map(earlier.submission_data.map((d) => [String(d.question_id), d]));
        const picked = [];
        for (const q of st.questions) {
          const d = by.get(String(q.id));
          if (!d || INFO.has(q.question_type)) continue;
          const answer = histAnswer(q, d);
          if (answer === null || answer === undefined) continue;
          picked.push([q, answer]);
        }
        if (!picked.length) { U.toast(`Attempt ${earlier.attempt} has no answers these questions take.`); return; }
        await settled();
        for (const [q, answer] of picked) q.answer = answer;
        const batchErr = await inTurn(() => store.quizApi.answerMany(st.sub, picked.map(([q, answer]) => ({ id: q.id, answer })), codeFor())).then(() => null, (e) => e);
        // (one answer Canvas refuses turns the whole request away: each is then sent on its own, and only that one stays out)
        const missing = await mendAnswers(picked.map(([q]) => q));
        if (batchErr && missing === null) throw batchErr;
        if (!ctx.alive()) return;
        draw();
        const n = picked.length - (missing?.length || 0);
        if (missing?.length) U.toast(`Imported ${n} of ${picked.length} answers from attempt ${earlier.attempt}. Canvas did not keep ${missing.map((q) => `Question ${st.questions.indexOf(q) + 1}`).join(', ')}.`, { error: true });
        else U.toast(`Imported ${n} ${n === 1 ? 'answer' : 'answers'} from attempt ${earlier.attempt}.`);
      } catch (e) {
        U.toast(`Could not import the answers: ${e.message}`, { error: true });
      } finally {
        devBtn.disabled = false;
      }
    }
    async function toggleFlag(q) {
      const on = !q.flagged;
      q.flagged = on;
      paintProgress();
      try {
        await inTurn(() => store.quizApi.flag(st.sub, q.id, on, codeFor()));
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
      // The intro sits in the course's column, under its header; the attempt, the receipt and (2.98.27)
      // the feedback take the page — the feedback's own rail and cards need the width a column leaves.
      const embedded = st.stage === 'intro';
      const switchable = st.stage === 'take' && !phone; // (a phone shows one question a screen, always)
      modeWrap.replaceChildren(...(switchable ? [
        modeBtn('side', 'Controls at the sides', MODE_SIDE),
        modeBtn('one', 'One question at a time', MODE_ONE),
        forcedOne ? null : modeBtn('all', 'Scroll through all questions', MODE_ALL),
      ].filter(Boolean) : []));
      modeWrap.hidden = !switchable;
      devBtn.hidden = !(devImport && st.stage === 'take' && st.sub);
      const side = st.stage === 'take' && st.mode === 'side';
      if (!side && progressWrap.parentElement !== head) head.append(progressWrap); // (the side layout keeps the pills in its left rail)
      screen.classList.toggle('is-side', side);
      instrBtn.hidden = st.stage === 'intro' || st.stage === 'feedback'; // the intro card already has them in front of you; the feedback is about the answers
      rawBtn.hidden = simulated || st.stage === 'feedback'; // (the simulation has no page on Canvas)
      const closeWord = st.stage === 'feedback' ? 'Back to the quiz' : 'Save and exit';
      exitBtn.title = closeWord;
      exitBtn.setAttribute('aria-label', closeWord);
      paintProgress();
      body.classList.toggle('is-busy', st.loadingIdx !== null && st.stage === 'take');
      if (st.stage === 'intro') body.replaceChildren(intro());
      else if (st.stage === 'starting') body.replaceChildren(h('p', { class: 'bcv-qz__starting', text: st.sub ? 'Resuming your attempt…' : 'Starting your attempt…' }));
      else if (st.stage === 'take') body.replaceChildren(st.mode === 'all' ? takeAll() : st.mode === 'side' ? takeSide() : takeOne());
      else if (st.stage === 'review') body.replaceChildren(review());
      else if (st.stage === 'feedback') body.replaceChildren(feedback());
      else body.replaceChildren(done());
      screen.classList.toggle('is-feedback', st.stage === 'feedback');
      screen.classList.toggle('is-embedded', embedded);
      html.classList.toggle('bcv-quiz', !embedded); // the attempt (and its receipt) takes the page
      setOpen(st.stage === 'take' || st.stage === 'review');
    }
    function modeBtn(key, label, icon) {
      return h('button', { type: 'button', class: `bcv-qz__mode ${st.mode === key ? 'is-active' : ''}`, title: label, 'aria-label': label, 'aria-pressed': st.mode === key ? 'true' : 'false', dataset: { mode: key }, onclick: () => { if (st.mode === key) return; st.mode = key; store.setPref('quizLayout', key); draw(); if (key !== 'all') toTop(); } }, U.svg(icon, { size: 15, width: 1.9 }));
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
      const side = st.mode === 'side' && !starting; // (a grid down the left rail)
      const list = st.questions.length ? st.questions : Array.from({ length: Math.max(1, Number(quiz.question_count) || 1) }, (_, k) => ({ id: `pending-${k}`, pending: true, flagged: false, answer: null }));
      progressWrap.replaceChildren(U.el(`bcv-qz__progress ${all ? 'bcv-qz__progress--all' : side ? 'bcv-qz__progress--side' : ''}`, list.map((q, k) => {
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
      // Canvas tells a student a quiz has a code (has_access_code) but not the code; where it does not
      // say, the refusal at Begin does (st.needsCode), and the field appears then
      const needsCode = !!(quiz.access_code || quiz.has_access_code || st.needsCode);
      if (needsCode && !(st.code || '').trim()) st.code = remembered(); // (an attempt resumed in this tab: the code is still known)
      const lockdown = !!quiz.require_lockdown_browser;
      // a survey: no right answers, points (if any) for taking part, and the words say so
      const survey = /survey/.test(quiz.quiz_type || '');
      const gradedSurvey = quiz.quiz_type === 'graded_survey';
      const ptsLine = survey ? (gradedSurvey ? `${pts} for taking part` : 'not graded') : pts;
      let refreshBegin = () => {};
      const bullets = [
        noBack ? ['#ff9500', IC.lock, 'This quiz seals each question once you leave it: an answer cannot be changed and you cannot go back.'] : null,
        survey ? ['#34c759', CHECK, 'A survey has no right answers. Your responses are saved as you go, and you can leave and come back.'] : ['#34c759', CHECK, 'Answers save as you pick them. You can leave and come back.'],
        quiz.anonymous_submissions ? ['var(--bcv-ink3)', IC.people, 'Your responses are anonymous.'] : null,
        timed ? ['#ff9500', IC.warn, `Time limit: ${quiz.time_limit} minutes. The clock starts when you begin and keeps running if you leave.`] : null,
        forcedOne ? ['var(--bcv-ink3)', MODE_ONE, noBack ? 'One question at a time, and you cannot go back to a previous question.' : 'One question at a time.'] : null,
        attemptsLeft !== null ? ['var(--bcv-ink3)', IC.bolt, attemptsLeft > 0 ? `${U.plural(attemptsLeft, 'attempt')} left of ${allowed}.` : `No attempts left — this quiz allows ${U.plural(allowed, 'attempt')}.`] : ['var(--bcv-ink3)', IC.bolt, 'Unlimited attempts.'],
        quiz.lock_at ? ['var(--bcv-ink3)', IC.lock, `Available until ${U.fmtAt(quiz.lock_at)}.`] : null,
        // the restrictions Canvas lets an instructor set: an access code, an IP filter, LockDown Browser
        needsCode ? ['#ff9500', IC.lock, 'This quiz needs an access code from your instructor.'] : null,
        quiz.ip_filter ? ['#ff9500', IC.lock, 'This quiz can only be taken from an allowed network, such as the classroom or campus.'] : null,
        lockdown ? ['#ff9500', IC.lock, 'This quiz requires Respondus LockDown Browser. Open Canvas in that browser to take it.'] : null,
      ].filter(Boolean);
      const codeInput = needsCode ? h('input', { class: 'bcv-input bcv-qz__code', type: 'text', placeholder: 'Access code', value: st.code || '', autocomplete: 'off', spellcheck: 'false', 'aria-label': 'Access code', oninput: (e) => { st.code = e.target.value; refreshBegin(); }, onkeydown: (e) => { if (e.key === 'Enter') { e.preventDefault(); begin(); } } }) : null;
      const canStart = !quiz.locked_for_user && !lockdown && (attemptsLeft === null || attemptsLeft > 0 || !!st.sub);
      const codeMissing = () => needsCode && !(st.code || '').trim(); // the code comes first: Begin waits for it
      const beginLabel = () => (canStart && codeMissing() ? 'Enter the access code' : st.sub ? (survey ? 'Continue survey' : 'Continue attempt') : canStart ? (survey ? 'Begin survey' : 'Begin attempt') : lockdown ? 'Needs LockDown Browser' : 'No attempts left');
      const lastDone = canStart ? null : latestFinished();
      // out of attempts: the primary action becomes the feedback for the last one (when released)
      const startBtn = !canStart && lastDone && !survey && !resultsHidden(lastDone)
        ? h('button', { type: 'button', class: 'bcv-qz__begin', text: 'See your feedback', onclick: () => openFeedback(lastDone, 'intro') })
        : h('button', { type: 'button', class: 'bcv-qz__begin', text: beginLabel(), disabled: (!canStart || codeMissing()) || null, dataset: { begin: '1' }, onclick: begin });
      refreshBegin = () => { if (startBtn.dataset.begin) { startBtn.disabled = (!canStart || codeMissing()) || null; startBtn.textContent = beginLabel(); } };
      return U.el('bcv-qz__intro', [
        h('div', {}, [
          h('h1', { class: 'bcv-qz__h1 bcv-pretty', text: quiz.title }),
          h('p', { class: 'bcv-qz__lead', text: `${course.name} · ${U.plural(quiz.question_count || 0, 'question')} · ${ptsLine} · ${quiz.due_at ? `Due ${U.fmtAtUpper(quiz.due_at)}` : 'No due date'}` }), // ("Sep 25 at 10:30 AM", the day first, as every other due line reads)
        ]),
        U.card(U.el('bcv-qz__before', [
          U.text('bcv-qz__kicker', 'Before you start'),
          quiz.description ? BCV.screens.course.prose(quiz.description, { cls: 'bcv-qz__desc' }) : h('p', { class: 'bcv-qz__p', text: 'Read each question carefully. Your answers are sent to Canvas as you pick them.' }),
          U.el('bcv-qz__bullets', bullets.map(([color, icon, text]) => U.el('bcv-qz__bullet', [U.svg(icon, { size: 18, stroke: color, width: 2, style: { flex: 'none', marginTop: '1px' } }), h('span', { class: 'bcv-pretty', text })]))),
          codeInput,
          st.codeErr ? U.text('bcv-error bcv-qz__codeerr', st.codeErr) : null,
          quiz.locked_for_user ? U.text('bcv-error', quiz.lock_explanation ? htmlToText(quiz.lock_explanation, 200) : 'This quiz is locked.') : null,
        ]), 'bcv-card--22'),
        startBtn,
        h('p', { class: 'bcv-qz__note', text: st.sub ? `Started ${U.fmtTime(st.sub.started_at)} · attempt ${st.sub.attempt}` : (attemptsLeft !== null ? (attemptsLeft > 0 ? `Attempt ${limit.used + 1} of ${allowed}` : `${U.plural(limit.used, 'attempt')} used of ${allowed}`) : 'Take your time — nothing is submitted until you say so.') }),
      ]);
    }

    async function begin() {
      if ((quiz.access_code || quiz.has_access_code || st.needsCode) && !(st.code || '').trim()) { st.codeErr = 'Enter the access code first.'; draw(); return; } // required before an attempt
      // the popup opens at once, its pills standing for the questions to come, the first filling while they load
      st.stage = 'starting';
      st.loadingIdx = 0;
      draw();
      try {
        st.sub = await store.quizApi.start(cid, qid, st.code);
        st.codeErr = '';
        if ((st.code || '').trim()) remember(st.code.trim());
        if (!st.paged) {
          try {
            st.questions = (await store.quizApi.questions(st.sub)).map(tidy);
          } catch (e) {
            if (!/one question at a time/i.test(e.message || '')) throw e;
            st.paged = true; // the quiz did not say so, but Canvas did
            forcedOne = true;
            if (st.mode === 'all') st.mode = 'side';
          }
        }
        if (st.paged) applyPage(await QP().fetchPage(quizUrl, { accessCode: codeFor() }));
        else pointsFromPage(); // (not waited for: the questions show at once, their points follow)
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
        const msg = String(e?.message || '');
        // a restricted quiz refuses in words: the code field appears (or says the code was wrong), an IP filter is explained
        if (/access code/i.test(msg)) {
          st.codeErr = st.code ? 'That access code was refused. Check it with your instructor.' : 'This quiz needs an access code. Enter it above, then begin.';
          st.needsCode = true;
          draw();
          setTimeout(() => { const f = document.querySelector('.bcv-qz__code'); if (f) { f.focus(); f.select(); } }, 60);
          return;
        }
        if (/ip address|ip filter|from your (ip|location|network)/i.test(msg)) {
          st.codeErr = 'Canvas only allows this quiz from certain networks (an IP filter). Try from the classroom or campus network.';
          draw();
          return;
        }
        draw();
        U.toast(`Could not start the attempt: ${msg}`, { error: true });
      }
    }
    /** The points each question is worth. Canvas's API keeps them from a student — it censors every
     *  question it hands one — so they are read off Canvas's own take page for this attempt, the same
     *  numbers ("4 pts") it shows beside each question there. Asked for with the attempt already open,
     *  that page only draws it. Quietly: if it will not come, the questions simply go without. */
    async function pointsFromPage() {
      if (simulated || st.paged || !st.questions.some((q) => !hasNum(q.points_possible))) return;
      const pg = await QP().fetchPage(quizUrl, { accessCode: codeFor() }).catch(() => null);
      if (!ctx.alive() || !pg?.ok) return;
      const pts = new Map(pg.questions.filter((q) => hasNum(q.points_possible)).map((q) => [String(q.id), Number(q.points_possible)]));
      for (const q of st.questions) if (!hasNum(q.points_possible) && pts.has(String(q.id))) q.points_possible = pts.get(String(q.id));
      // painted in place: a redraw would take the cursor out of an answer being typed
      for (const el of screen.querySelectorAll('.bcv-qz__qof[data-qid]')) {
        const q = st.questions.find((x) => String(x.id) === el.dataset.qid);
        if (q) el.textContent = qofText(q);
      }
    }
    const qofText = (q) => `of ${st.questions.length}${hasNum(q.points_possible) ? ` · ${store.fmtPts(q.points_possible)} ${Number(q.points_possible) === 1 ? 'point' : 'points'}` : ''}`;
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
        if (full) return tidy({ ...full, position: k + 1 });
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
        const pg = how.action ? await QP().advance(how.action, fields) : await QP().fetchPage(quizUrl, { questionId: how.questionId, accessCode: codeFor() });
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

    /** A question: its number on top, its text, its answers. `side`: the side layout's — the number
     *  large over the question, the text at Canvas's own size and in its own lines (images, tables and
     *  videos at the size their author gave), the flag in the right rail instead of here. */
    function questionBlock(q, k, { compact = false, side = false } = {}) {
      const flagBtn = side ? null : h('button', { type: 'button', class: `bcv-qz__flag ${q.flagged ? 'is-on' : ''}`, onclick: () => toggleFlag(q) }, [U.svg(FLAG, { size: compact ? 12 : 13, width: 2 }), h('span', { text: q.flagged ? (compact ? 'Flagged' : 'Flagged for review') : (compact ? 'Flag' : 'Flag for review') })]);
      const headRow = U.el(`bcv-qz__qhead ${side ? 'bcv-qz__qhead--side' : ''}`, [
        h('span', { class: 'bcv-qz__qnum', text: `Question ${k + 1}` }),
        h('span', { class: 'bcv-qz__qof', dataset: { qid: String(q.id) }, text: qofText(q) }),
        INFO.has(q.question_type) ? null : flagBtn,
      ]);
      const text = BCV.screens.course.prose(q.question_text || q.question_name || '', { cls: `bcv-qz__qtext ${side ? 'bcv-qz__qtext--canvas' : compact ? 'bcv-qz__qtext--compact' : ''}` });
      const area = answerArea(q, compact || side);
      weave(text, area, q);
      const left = area && area.bcvBlanks && !area.children.length ? null : area; // (every blank went into the sentence)
      return h('div', { id: `bcv-q${k}`, class: 'bcv-qz__q' }, [headRow, text, left]);
    }

    /* A blank belongs in the sentence it was written into, not in a list underneath it.
     *
     * Canvas's own take page puts the field inline — a dropdown in the middle of the question — and
     * that markup arrives with the question text, so the question showed its blanks twice: Canvas's
     * own control in the sentence, dead (its page sets the value with a script of its own, so it
     * always read "[ Select ]"), and the row below it, live. The API gives the same question as it
     * was written, with [name] where the field goes. Either way the field built for that blank is
     * moved into the gap and the row it came from goes. A blank the text never names keeps its row,
     * so nothing is left with no way to answer it. */
    function weave(textEl, areaEl, q) {
      const holds = areaEl && areaEl.bcvBlanks;
      if (!holds || !holds.size) return;
      const free = () => [...holds.keys()].filter((b) => !holds.get(b).placed);
      const put = (name, slot) => {
        const held = name == null ? null : holds.get(String(name));
        if (!held || held.placed) return false;
        held.placed = true;
        held.field.classList.add('bcv-qz__inblank');
        slot.replaceWith(held.field);
        held.row.remove();
        return true;
      };
      /* Which blank a control in the sentence stands for. Not its name: Canvas calls the field
       * question_<id>_<hash of the blank>, so the name says nothing a reader can match — which is
       * what left the fields stripped out and the rows still underneath. A dropdown says it outright
       * instead, in the answers it offers: those ids belong to one blank and no other. Failing that
       * (a line to type in offers nothing), the controls stand in the order the blanks do. */
      const ids = new Map([...holds.keys()].map((b) => [b, new Set((q.answers || []).filter((a) => String(a.blank_id) === b).map((a) => String(a.id)))]));
      const whose = (w) => {
        if (w.tagName === 'SELECT') {
          const vals = [...w.options].map((o) => String(o.value)).filter(Boolean);
          const hit = vals.length ? free().find((b) => vals.every((v) => ids.get(b).has(v))) : null;
          if (hit) return hit;
        }
        const tail = (w.getAttribute('name') || '').replace(`question_${q.id}_`, '');
        if (holds.has(tail) && !holds.get(tail).placed) return tail;
        return free()[0] || null;
      };
      const head = `question_${q.id}_`;
      for (const w of [...textEl.querySelectorAll(`select[name^="${head}"], input[name^="${head}"], textarea[name^="${head}"], select.question_input, input.question_input`)]) {
        if (!put(whose(w), w)) w.remove(); // Canvas's own control answers nothing here
      }
      const spare = free();
      if (!spare.length) return;
      const token = new RegExp(`\\[(${spare.map((b) => b.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})\\]`);
      const walk = document.createTreeWalker(textEl, NodeFilter.SHOW_TEXT);
      const hits = [];
      for (let n = walk.nextNode(); n; n = walk.nextNode()) if (token.test(n.nodeValue)) hits.push(n);
      for (const hit of hits) {
        let node = hit;
        for (let m = token.exec(node.nodeValue); m; m = token.exec(node.nodeValue)) {
          const tok = node.splitText(m.index);
          const rest = tok.splitText(m[0].length);
          put(m[1], tok); // (a name written twice keeps the second one as it reads)
          node = rest;
        }
      }
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
          ? h('textarea', { class: 'bcv-textarea', rows: 6, placeholder: 'Your answer…', oninput: (e) => { const v = e.target.value; saveText(q, looksHtml(q.answer) || /\n/.test(v) || /^\s*([•\-*]|\d+[.)])\s+/.test(v) ? plainToHtml(v) : v); } })
          : h('input', { class: 'bcv-input', type: NUMERIC.has(type) ? 'number' : 'text', step: 'any', placeholder: NUMERIC.has(type) ? 'Number' : 'Your answer', oninput: (e) => saveText(q, NUMERIC.has(type) && e.target.value !== '' ? Number(e.target.value) : e.target.value) });
        field.value = q.answer === null || q.answer === undefined ? '' : isEssay && looksHtml(q.answer) ? htmlToPlain(q.answer) : String(q.answer);
        // a formula question says how precise the answer is to be, where the quiz says so
        const places = type === 'calculated_question' && Number.isInteger(Number(q.formula_decimal_places)) && q.formula_decimal_places !== null && q.formula_decimal_places !== '' ? Number(q.formula_decimal_places) : null;
        return U.el('bcv-qz__text', [field, places !== null ? U.text('bcv-qz__texthint', places ? `Give the answer to ${places} decimal ${places === 1 ? 'place' : 'places'}.` : 'Give the answer as a whole number.', 'span') : null]);
      }
      // Matching: each left-hand value with the same list of right-hand ones beside it. Canvas takes
      // the picks as pairs — the answer's own id against the match it was set to.
      if (MATCH.has(type)) {
        const matches = q.matches || [];
        const chosen = new Map(pairsOf(q.answer).map((p2) => [String(p2.answer_id), String(p2.match_id)]));
        const rows = [];
        const send = () => {
          const pairs = [];
          for (const [aid, sel] of rows) {
            const pair = { answer_id: whole(aid), match_id: whole(sel.bcvPicker ? sel.bcvPicker.value : sel.value) };
            if (Number.isInteger(pair.answer_id) && Number.isInteger(pair.match_id)) pairs.push(pair); // (a row with no pick sends nothing; Canvas takes whole numbers only)
          }
          save(q, pairs);
        };
        return U.el(`bcv-qz__match ${compact ? 'bcv-qz__match--compact' : ''}`, opts.map((a) => {
          const sel = U.picker(
            [{ value: '', text: 'Choose…' }, ...matches.filter((m) => hasId(m.match_id)).map((m) => ({ value: String(m.match_id), text: m.text, html: m.html || '' }))],
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
        const blanks = blanksOf(q);
        const held = q.answer && typeof q.answer === 'object' && !Array.isArray(q.answer) ? q.answer : {};
        const fields = [];
        const send = () => {
          const out = {};
          for (const [blank, f] of fields) {
            const v = String((f.bcvPicker ? f.bcvPicker.value : f.value) || '').trim();
            if (v) out[blank] = drops ? whole(v) : v;
          }
          save(q, out);
        };
        if (!blanks.length) return null;
        const holds = new Map();
        const wrap = U.el(`bcv-qz__blanks ${compact ? 'bcv-qz__blanks--compact' : ''}`, blanks.map((blank) => {
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
          const row = U.el('bcv-qz__blankrow', [U.text('bcv-qz__blanklbl', blank, 'span'), f]);
          holds.set(String(blank), { field: f, row, placed: false });
          return row;
        }));
        wrap.bcvBlanks = holds; // the sentence takes what it names (see weave)
        return wrap;
      }
      // A file: chosen here, uploaded to the student's own quiz files on Canvas (the three steps an
      // assignment's file takes), and the answer set to name it. Replaced by choosing another; taken
      // off with Remove. Canvas's own page is one press away for anyone who wants it there instead.
      if (FILE.has(type)) {
        const ids = (Array.isArray(q.answer) ? q.answer : []).map(String).filter(Boolean);
        const shownName = ids.length ? (st.files[ids[0]] || `File ${ids[0]}`) : '';
        const status = h('span', { class: 'bcv-qz__filename', text: ids.length ? shownName : 'No file yet' });
        const box = U.el(`bcv-qz__file ${ids.length ? 'has-file' : ''}`);
        const pick = async (f) => {
          if (!f) return;
          box.classList.add('is-busy');
          status.textContent = `Uploading ${f.name}…`;
          try {
            const id = await store.quizApi.uploadFile(cid, qid, f, (p) => { if (Number.isFinite(p)) status.textContent = `Uploading ${f.name}… ${Math.round(Math.min(1, p) * 100)}%`; });
            if (!ctx.alive()) return;
            st.files[String(id)] = f.name;
            await save(q, [whole(id)]);
            draw();
          } catch (err) {
            status.textContent = ids.length ? shownName : 'No file yet';
            U.toast(`Could not upload ${f.name}: ${err.message}`, { error: true });
          } finally {
            box.classList.remove('is-busy');
          }
        };
        const input = h('input', { type: 'file', class: 'bcv-qz__fileinput', 'aria-label': ids.length ? 'Replace the file' : 'Choose a file', onchange: (e) => { const f = e.target.files?.[0]; e.target.value = ''; pick(f); } });
        box.addEventListener('dragover', (e) => { if ([...(e.dataTransfer?.items || [])].some((i) => i.kind === 'file')) { e.preventDefault(); box.classList.add('is-over'); } });
        box.addEventListener('dragleave', () => box.classList.remove('is-over'));
        box.addEventListener('drop', (e) => { const f = e.dataTransfer?.files?.[0]; if (!f) return; e.preventDefault(); box.classList.remove('is-over'); pick(f); });
        box.append(...[
          U.svg('M12 16V5M7 10l5-5 5 5M5 19h14', { size: 20, width: 2, cls: 'bcv-qz__fileic' }),
          h('div', { class: 'bcv-qz__filebody' }, [status, U.text('bcv-qz__filehint', ids.length ? 'Handed in with this attempt when you submit.' : 'Choose a file, or drop one here.', 'span')]),
          h('label', { class: 'bcv-qz__filepick' }, [h('span', { text: ids.length ? 'Replace' : 'Choose file' }), input]),
          ids.length ? h('button', { type: 'button', class: 'bcv-qz__fileclear', text: 'Remove', onclick: async () => { await save(q, []); draw(); } }) : null,
        ].filter(Boolean)); // (append writes a null out as the word)
        return box;
      }
      if (INFO.has(type)) return null;
      // Any other kind (a question type Canvas adds later): Canvas's own page handles it on the same attempt.
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
      const fill = footerEl.querySelector('.bcv-qz__meterfill'); // (the side layout's bar of what is answered)
      if (fill) fill.style.width = `${st.questions.length ? Math.round((answeredCount() / st.questions.length) * 100) : 0}%`;
    }
    function footer(buttons) {
      footerEl = U.el('bcv-qz__foot', U.el('bcv-qz__footin', [h('span', { class: 'bcv-qz__answered' }), ...buttons]));
      paintFooter();
      return footerEl;
    }

    /** Moving from question k: whether it is the last, and Back and Next — through Canvas's own page
     *  on a paged attempt, in place otherwise. */
    function moves(k) {
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
      return { last, back, next };
    }
    const toReview = () => { st.stage = 'review'; draw(); toTop(); };

    function takeOne() {
      const q = cur();
      const k = st.idx;
      if (!q) return U.el('bcv-qz__page', U.emptyCard('This quiz has no questions.'));
      const { last, back, next } = moves(k);
      return h('div', { class: 'bcv-qz__stage' }, [
        U.el('bcv-qz__page', questionBlock(q, k)),
        footer([
          noBack ? null : h('button', { type: 'button', class: 'bcv-qz__btn', text: 'Back', disabled: k === 0 || null, onclick: back }),
          last
            ? h('button', { type: 'button', class: 'bcv-qz__btn bcv-qz__btn--primary', text: 'Review answers', onclick: toReview })
            : h('button', { type: 'button', class: 'bcv-qz__btn bcv-qz__btn--primary bcv-qz__btn--next', text: 'Next', onclick: next }),
        ]),
      ]);
    }

    /* The side layout (2.98.24): the page's width put to use. Down the left, every question as a
     * numbered square — answered, flagged, the one on screen — with a key to them; in the middle, the
     * question, its number large on top and its text as Canvas lays it out; down the right, every
     * control: how many are answered and whether it is saved, Next (Review answers on the last), Back,
     * the flag, and the review at any point. Both rails stay in view as the question scrolls. The same
     * pills, buttons and saves as the one-at-a-time layout, placed differently. */
    function takeSide() {
      const q = cur();
      const k = st.idx;
      if (!q) return U.el('bcv-qz__page', U.emptyCard('This quiz has no questions.'));
      const { last, back, next } = moves(k);
      const key = (cls, text) => U.el('bcv-qz__key', [h('span', { class: `bcv-qz__keydot ${cls}` }), h('span', { text })]);
      const left = h('aside', { class: 'bcv-qz__lrail', 'aria-label': 'Questions' }, [
        U.text('bcv-qz__railh', 'Questions', 'div'),
        progressWrap,
        U.el('bcv-qz__legend', [key('is-current', 'On screen'), key('is-answered', 'Answered'), key('is-flagged', 'Flagged'), key('', 'Not answered')]),
      ]);
      const flagBtn = INFO.has(q.question_type) ? null : h('button', { type: 'button', class: `bcv-qz__flag bcv-qz__flag--side ${q.flagged ? 'is-on' : ''}`, 'aria-pressed': q.flagged ? 'true' : 'false', onclick: () => toggleFlag(q) }, [U.svg(FLAG, { size: 14, width: 2 }), h('span', { text: q.flagged ? 'Flagged for review' : 'Flag for review' })]);
      footerEl = h('aside', { class: 'bcv-qz__foot bcv-qz__rrail', 'aria-label': 'Controls' }, [
        U.text('bcv-qz__railh', 'Progress', 'div'),
        h('span', { class: 'bcv-qz__answered' }),
        h('div', { class: 'bcv-qz__meter', 'aria-hidden': 'true' }, h('span', { class: 'bcv-qz__meterfill' })),
        U.el('bcv-qz__railbtns', [
          last
            ? h('button', { type: 'button', class: 'bcv-qz__btn bcv-qz__btn--primary', text: 'Review answers', onclick: toReview })
            : h('button', { type: 'button', class: 'bcv-qz__btn bcv-qz__btn--primary bcv-qz__btn--next', text: 'Next', onclick: next }),
          noBack ? null : h('button', { type: 'button', class: 'bcv-qz__btn', text: 'Back', disabled: k === 0 || null, onclick: back }),
        ]),
        flagBtn,
        last ? null : h('button', { type: 'button', class: 'bcv-qz__btn bcv-qz__btn--quiet', text: 'Review answers', title: 'See every answer before you submit. Nothing is submitted until you say so.', onclick: toReview }),
      ]);
      paintFooter();
      return h('div', { class: 'bcv-qz__stage bcv-qz__side' }, [
        left,
        U.el('bcv-qz__page bcv-qz__page--side', questionBlock(q, k, { side: true })),
        footerEl,
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
        const pairs = pairsOf(a);
        if (!pairs.length) return null;
        return pairs.map((p2) => ({ text: `${one(p2.answer_id).text || partText(one(p2.answer_id))} → ${nameOf(p2.match_id)}`, html: '' }));
      }
      // one value per blank, named for the blank it fills
      if (BLANKS.has(q.question_type)) {
        const filled = Object.entries(filledOf(a));
        if (!filled.length) return null;
        return filled.map(([blank, v]) => {
          const named = !/^[0-9a-f]{8,}$/i.test(blank); // a quiz read from Canvas's own page knows the blank only as a hash of its name
          const o = DROPS.has(q.question_type) ? opts.find((x) => String(x.id) === String(v) && String(x.blank_id) === String(blank)) : null;
          return { text: `${named ? `${blank}: ` : ''}${o ? (o.text || htmlToText(o.html || '', 60)) : v}`, html: '' };
        });
      }
      if (FILE.has(q.question_type)) return a.map((id) => ({ text: st.files[String(id)] || `File ${id}`, html: '' })); // (the name, where it was uploaded here)
      if (Array.isArray(a)) return a.map(one);
      if (CHOICE.has(q.question_type)) return [one(a)];
      if (looksHtml(a)) return [{ text: '', html: String(a), block: true }]; // (an essay written in Canvas's editor: formatting, not tags)
      return [{ text: String(a), html: '', block: q.question_type === 'essay_question' && (String(a).length > 90 || /\n/.test(String(a))) }];
    }
    /** The same, flattened to one line — for the review list and anywhere a plain string is wanted. */
    function answerText(q) {
      const parts = answerParts(q);
      return parts ? parts.map(partText).join(', ') : null;
    }

    /** The question on its one line of the review: its words and formulas, without the videos,
     *  pictures and tables that belong to the question itself. */
    function sumText(q) {
      const el = BCV.screens.course.prose(noFields(q.question_text, q), { cls: 'bcv-qz__sumrich' });
      for (const n of el.querySelectorAll('iframe, video, audio, table, .bcv-embed-open, img:not(.equation_image)')) n.remove();
      return el;
    }
    function review() {
      const blanks = st.questions.length - answeredCount();
      return U.el('bcv-qz__review', [
        h('div', {}, [
          h('h1', { class: 'bcv-qz__h1 bcv-qz__h1--30', text: 'Review before submitting' }),
          h('p', { class: 'bcv-qz__lead', text: `${answeredLabel()} · ${blanks === 0 ? 'nothing left blank' : `${blanks} left blank`}. Tap any question to change it.` }),
        ]),
        U.card(st.questions.map((q, k) => {
          const parts = INFO.has(q.question_type) ? null : answerParts(q);
          const txt = answerText(q);
          // the answer as it was given: its own words, or Canvas's own content where the words are a
          // formula (an equation image with the LaTeX on the tag — the image is shown, never the LaTeX)
          const cell = h('span', { class: `bcv-qz__suma ${txt === null && !INFO.has(q.question_type) && !isAnswered(q) ? 'is-blank' : ''}` });
          if (INFO.has(q.question_type)) cell.textContent = '—';
          else if (parts) parts.forEach((p2, i) => { if (i) cell.append(', '); if (String(p2.text || '').trim()) cell.append(p2.text); else if (String(p2.html || '').trim()) cell.append(BCV.screens.course.prose(p2.html, { cls: 'bcv-qz__sumrich' })); else cell.append('—'); });
          else cell.textContent = isAnswered(q) ? 'Answered' : 'Not answered';
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
            h('span', { class: 'bcv-qz__sumq bcv-ellip' }, String(q.question_text || '').trim() ? sumText(q) : h('span', { text: q.question_name || '' })), // (the question as Canvas holds it: a formula in it is the formula, not its LaTeX)
            q.flagged ? U.svg(FLAG, { size: 13, stroke: '#ff9500', width: 2, style: { flex: 'none' } }) : null,
            cell,
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
        await settled(); // (an answer typed a moment ago is still on its pause, or on its way: it goes in before the attempt closes)
        // and every answer given here is on Canvas: one it does not hold is sent again first, and one that
        // will not stay is named before anything is handed in
        const missing = await mendAnswers(st.questions);
        if (missing?.length && !window.confirm(`${missing.map((q) => `Question ${st.questions.indexOf(q) + 1}`).join(', ')}: Canvas did not keep ${missing.length === 1 ? 'that answer' : 'those answers'}.\n\nSubmit anyway? Cancel to go back and answer ${missing.length === 1 ? 'it' : 'them'} again.`)) {
          st.stage = 'review';
          draw();
          return;
        }
        st.done = await store.quizApi.complete(cid, qid, st.sub, codeFor());
        // the attempt just handed in may be one whose grade waits to be posted: the receipt asks first
        if (quiz.assignment_id && !simulated) {
          const fresh = await store.submission(cid, quiz.assignment_id, { force: true }).catch(() => null);
          if (fresh) st.held = heldBack(fresh); // (not read: what was known stands)
        }
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
      const survey = /survey/.test(quiz.quiz_type || '');
      const gradedSurvey = quiz.quiz_type === 'graded_survey';
      const scoreVisible = !survey && !quiz.hide_results && !st.held && d.score !== null && d.score !== undefined && d.workflow_state !== 'pending_review';
      const feedbackOn = scoreVisible && d.id && !resultsHidden(d);
      return U.el('bcv-qz__done', [
        U.el('bcv-qz__donemark', U.svg(CHECK, { size: 34, stroke: '#34c759', width: 2.4 })),
        h('div', {}, [
          h('h1', { class: 'bcv-qz__h1 bcv-qz__h1--28', text: survey ? 'Responses recorded' : 'Attempt submitted' }),
          h('p', { class: 'bcv-qz__lead bcv-pretty', text: `${quiz.title} · submitted ${U.fmtAtUpper(d.finished_at || new Date())}. ${survey ? (gradedSurvey ? 'Thanks for taking part — the points for it post to your grades.' : 'Thanks for taking part.') : scoreVisible ? '' : 'Your score posts once your instructor releases it.'}` }),
        ]),
        U.el('bcv-qz__donecard', [h('span', { class: 'bcv-qz__donek', text: 'Questions answered' }), h('span', { class: 'bcv-qz__donev', text: answeredLabel() })]),
        scoreVisible ? U.el('bcv-qz__donecard', [h('span', { class: 'bcv-qz__donek', text: 'Score' }), h('span', { class: 'bcv-qz__donev', text: `${store.fmtPts(d.kept_score ?? d.score)} / ${store.fmtPts(quiz.points_possible || 0)}` })]) : null,
        gradedSurvey && quiz.points_possible ? U.el('bcv-qz__donecard', [h('span', { class: 'bcv-qz__donek', text: 'For taking part' }), h('span', { class: 'bcv-qz__donev', text: `${store.fmtPts(quiz.points_possible)} ${Number(quiz.points_possible) === 1 ? 'point' : 'points'}` })]) : null,
        U.el('bcv-qz__donebtns', [
          // feedback only once Canvas has released it (hide_results); the receipt says so otherwise. A survey has none.
          feedbackOn ? h('button', { type: 'button', class: 'bcv-qz__big bcv-qz__big--primary', text: 'See feedback', onclick: () => openFeedback(d, 'done') }) : null,
          h('button', { type: 'button', class: `bcv-qz__big ${feedbackOn ? '' : 'bcv-qz__big--primary'}`, text: survey ? 'Back to the survey' : 'Back to the quiz', onclick: () => exitTo(simulated ? simHome : quizUrl) }),
        ]),
      ]);
    }

    // ---- feedback (mockup 9) ---------------------------------------------------------
    // A finished attempt, question by question: your answer, the correct one when the
    // quiz's settings allow it, the instructor's worked solution. Everything comes from the attempt's own question data
    // and the assignment submission's comments; nothing is fetched beyond that.
    const CS = () => BCV.screens.course;
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
      if (st.held) return HELD_LINE;
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
    /** The student's answer as the graded history records it: answer_id, answer_<id> — a "1" per
     *  option ticked (multiple answers) or the match each left-hand value was set to (matching) —
     *  answer_for_<blank> and answer_id_for_<blank> (the blank kinds), or text. */
    function histAnswer(q, d) {
      if (FILE.has(q.question_type)) { const ids = (Array.isArray(d.attachment_ids) ? d.attachment_ids : []).filter(hasId).map(whole); return ids.length ? ids : null; }
      if (MULTI.has(q.question_type)) {
        const on = Object.keys(d).filter((k) => /^answer_\d+$/.test(k) && String(d[k]) === '1').map((k) => Number(k.slice(7)));
        return on.length ? on : null;
      }
      if (MATCH.has(q.question_type)) {
        const pairs = Object.keys(d).filter((k) => /^answer_\d+$/.test(k) && hasId(d[k])).map((k) => ({ answer_id: whole(k.slice(7)), match_id: whole(d[k]) }));
        return pairs.length ? pairs : null;
      }
      if (BLANKS.has(q.question_type)) {
        const out = {};
        for (const k of Object.keys(d)) {
          const blank = /^answer_for_(.+)$/.exec(k)?.[1];
          if (!blank) continue;
          const v = DROPS.has(q.question_type) && hasId(d[`answer_id_for_${blank}`]) ? d[`answer_id_for_${blank}`] : d[k];
          if (hasId(v)) out[blank] = v;
        }
        return Object.keys(out).length ? out : null;
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
        if (a.answer !== undefined && a.answer !== null && a.answer !== '') return { text: String(a.answer), html: '' }; // (a formula's result for this attempt)
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
    /** The correct answer(s) as Canvas's results page marks them, in the pieces answerParts gives: an
     *  option's own words from the question where it has them (the page's otherwise). */
    function pageRight(q, pq) {
      if (!pq?.right?.length) return null;
      const opts = new Map((q.answers || []).map((a) => [String(a.id), a]));
      const parts = pq.right.map((x) => {
        const o = opts.get(String(x.id));
        const own = o && (String(o.text || '').trim() || String(o.html || '').trim()) ? { text: o.text || '', html: o.html || '' } : { text: x.text || '', html: x.html || '' };
        return String(own.text).trim() || String(own.html).trim() ? { ...own, text: x.blank && BLANKS.has(q.question_type) && String(own.text).trim() ? `${x.blank}: ${own.text}` : own.text } : null;
      }).filter(Boolean);
      return parts.length ? parts : null;
    }
    const hasNum = (v) => v !== null && v !== undefined && v !== '' && Number.isFinite(Number(v));
    async function loadFeedback(sub) {
      // Canvas's questions carry the latest attempt's answers and marks alone: an earlier attempt
      // asked for is read from the graded history, its own answers and its own ticks
      const live = (subs || [])[0];
      const older = !!live && Number(sub.attempt) !== Number(live.attempt);
      const [qs, asub, page] = await Promise.all([
        store.quizApi.questions(sub, { courseId: cid, quizId: qid }),
        quiz.assignment_id ? store.submission(cid, quiz.assignment_id, { force: true }).catch(() => null) : Promise.resolve(null),
        // what the API keeps from a student (each question's points, the right answers, the comments): Canvas's own results page
        simulated ? Promise.resolve(null) : QP().results(cid, qid, sub).catch(() => null),
      ]);
      // held back since the page was drawn, or before: nothing of the attempt's grading is shown
      if (heldBack(asub)) { st.held = true; return { sub, held: true }; }
      const me = String(store.env().current_user_id || '');
      const comments = (asub?.submission_comments || []).filter((c) => !me || String(c.author_id ?? '') !== me);
      // per-question points, when the assignment submission's history carries this attempt's grading
      const hist = (asub?.submission_history || []).find((x) => Number(x.attempt) === Number(sub.attempt)) || null;
      const graded = new Map((hist?.submission_data || []).map((d) => [String(d.question_id), d]));
      const rows = qs.map(tidy).map((q, k) => { // (tidy: a matching answer of empty rows, or blanks all empty, is no answer — so the graded history is read instead)
        const d = graded.get(String(q.id)) || null;
        const pq = page?.get(String(q.id)) || null;
        if (d && (older || !answered(q.answer))) q.answer = histAnswer(q, d); // (a one-at-a-time quiz's answers come from the graded history too)
        const flag = older && d ? parseCorrect(d.correct) : (parseCorrect(q.correct) ?? (d ? parseCorrect(d.correct) : null));
        // the points a question was worth: the API's where it gives them (it never does to a student), else the results page's
        const possible = hasNum(q.points_possible) ? Number(q.points_possible) : hasNum(pq?.possible) ? Number(pq.possible) : null;
        // the points it got decide how it went: Canvas's right/wrong flag can lag behind them (a regrade, a
        // score the instructor changed), and an answer given full points is right whatever the flag says.
        // An answer not graded yet (an essay: no flag, no points) stays unmarked.
        const pts = d && hasNum(d.points) ? Number(d.points) : null;
        const correct = pts !== null && possible > 0 && (flag !== null || pts > 0) ? (pts >= possible - 1e-9 ? true : pts > 0 ? 'partial' : false) : flag;
        const earned = pts !== null ? pts : correct === true ? possible : correct === false ? 0 : null;
        const rightIds = new Set([...(q.answers || []).filter((a) => Number(a.weight) === 100).map((a) => String(a.id)), ...(pq?.right || []).map((x) => String(x.id))]);
        const sol = fbSolution(q, correct === true) || (pq?.comment ? { html: pq.comment } : null);
        return { q, k, correct, possible, earned, yours: answerParts(q), right: fbRight(q) || pageRight(q, pq), rightIds, shown: correctVisible() || !!pq?.shown, match: pq?.match || null, sol, read: !!pq, info: INFO.has(q.question_type) };
      }).filter((r) => !r.info);
      const released = rows.some((r) => r.correct !== null);
      const possible = Number(quiz.points_possible) || rows.reduce((s, r) => s + (r.possible || 0), 0);
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
      const marksRight = r.shown && r.correct !== null;
      const list = U.el('bcv-fb__opts', opts.map((o, j) => {
        const picked = mine.has(String(o.id));
        const right = marksRight && r.rightIds.has(String(o.id));
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

    /* ---- the feedback screen (2.98.27) ----------------------------------------------
     * The page is the attempt's, as the attempt itself had it. Down the left, a rail that stays in view:
     * the score, a filter (every question, the ones to look at again, the ones got right) and every
     * question as a numbered square coloured by how it went, the one being read outlined — press one
     * to go to it; J and K go on and back. On the right the questions themselves, at Canvas's reading
     * size: what the question asked, then your answer and the correct one side by side (a matching
     * question as a table, a row per value), the options, the worked solution. */
    const verdictOf = (r) => (r.correct === true ? 'right' : r.correct === false ? 'wrong' : r.correct === 'partial' ? 'partial' : 'none');
    const VERDICT = { right: 'Correct', wrong: 'Incorrect', partial: 'Partly right', none: 'Not marked yet' };
    const MARK = { right: CHECK, wrong: CROSS, partial: 'M5 12h14', none: 'M12 17h.01M9.5 9.5a2.5 2.5 0 015 0c0 1.6-2.5 2-2.5 4' };
    const inkOf = (v) => {
      const dark = app.isDark();
      return v === 'right' ? (dark ? ['#5ddb7d', 'rgba(52,199,89,.2)'] : ['#1e7a37', 'rgba(52,199,89,.14)'])
        : v === 'wrong' ? (dark ? ['#ff8098', 'rgba(255,45,85,.2)'] : ['#c01d43', 'rgba(255,45,85,.12)'])
          : v === 'partial' ? ['#ff9500', 'rgba(255,149,0,.16)'] : ['var(--bcv-ink3)', 'var(--bcv-fill)'];
    };
    /** An answer's pieces, one after another: its own words, or Canvas's own content where it has none. */
    function ansBody(parts) {
      if (!parts?.length) return [h('span', { class: 'bcv-qfb__noans', text: 'No answer' })];
      const out = [];
      parts.forEach((p, i) => {
        if (i) out.push(h('span', { class: 'bcv-qfb__sep', text: ', ' }));
        if (String(p.text || '').trim()) out.push(h('span', { text: p.text }));
        else if (String(p.html || '').trim()) out.push(CS().prose(p.html, { cls: 'bcv-fb__chiprich' }));
        else out.push(h('span', { text: '—' }));
      });
      return out;
    }
    /** A matching answer as a table: each value, the match you set, and — where the quiz shows correct
     *  answers and one was missed — the right match, each row marked. */
    function fbMatchTable(r, wantRight) {
      const q = r.q;
      const set = new Map(pairsOf(q.answer).map((p) => [String(p.answer_id), String(p.match_id)]));
      const nameOf = (mid) => (q.matches || []).find((m) => String(m.match_id) === String(mid))?.text ?? null;
      const rows = (q.answers || []).map((a) => {
        const mid = set.get(String(a.id));
        const mine = mid === undefined ? null : (nameOf(mid) ?? mid);
        // the right match: the question's own where it has one (a student's never does), else the results page's —
        // a row it marks right was matched right, and under a wrong one it writes the match that was
        const pr = r.match?.get(String(a.id)) || null;
        const right = wantRight ? (String(a.right || '').trim() || (hasId(a.match_id) ? nameOf(a.match_id) : null) || pr?.right || (pr?.ok === true ? mine : null)) : null;
        return { a, mine, right, ok: pr?.ok ?? null };
      });
      const showRight = rows.some((x) => x.right !== null);
      return h('table', { class: 'bcv-qfb__match' }, [
        h('thead', {}, h('tr', {}, [h('th', { text: 'Item' }), h('th', { text: 'Your match' }), showRight ? h('th', { text: 'Correct match' }) : null])),
        h('tbody', {}, rows.map(({ a, mine, right, ok: marked }) => {
          const ok = !showRight ? null : marked !== null ? marked : right !== null && mine !== null ? String(mine).trim() === String(right).trim() : null;
          return h('tr', { class: ok === true ? 'is-right' : ok === false || (showRight && mine === null) ? 'is-wrong' : '' }, [
            h('td', { class: 'bcv-qfb__mleft' }, a.html ? CS().prose(a.html, { cls: 'bcv-fb__chiprich' }) : h('span', { text: a.text || a.left || '—' })),
            h('td', { class: `bcv-qfb__mmine ${mine === null ? 'is-empty' : ''}`, text: mine ?? 'No match set' }),
            showRight ? h('td', { class: 'bcv-qfb__mright', text: right ?? '—' }) : null,
          ]);
        })),
      ]);
    }
    /** What you answered, and — when the quiz shows it and you missed it — the correct answer beside it. */
    function fbAnswers(r) {
      const q = r.q;
      const v = verdictOf(r);
      const reveal = v !== 'right' && r.correct !== null && r.shown;
      if (MATCH.has(q.question_type) && (q.answers || []).length) return U.el('bcv-qfb__answers', fbMatchTable(r, reveal));
      const showRight = reveal && !!r.right?.length;
      const essay = r.yours?.some((p) => p.block);
      const [ink, tint] = inkOf(v);
      const yours = U.el('bcv-qfb__ans is-yours', [
        U.text('bcv-qfb__anslbl', 'Your answer', 'span'),
        essay ? U.el('bcv-fb__essay', r.yours.map((p) => (p.html ? CS().prose(p.html, { cls: 'bcv-fb__essaybody bcv-prose' }) : h('p', { class: 'bcv-fb__essaybody', text: p.text }))))
          : h('div', { class: 'bcv-qfb__ansval', style: { color: v === 'none' ? '' : ink } }, ansBody(r.yours)),
      ]);
      if (!essay && v !== 'none') yours.style.background = tint;
      return U.el(`bcv-qfb__answers ${showRight ? 'bcv-qfb__answers--two' : ''}`, [
        yours,
        showRight ? U.el('bcv-qfb__ans is-right', [U.text('bcv-qfb__anslbl', 'Correct answer', 'span'), h('div', { class: 'bcv-qfb__ansval' }, ansBody(r.right))]) : null,
      ]);
    }
    function fbCard(r, i) {
      const v = verdictOf(r);
      const [ink, tint] = inkOf(v);
      const part = v === 'partial';
      // (the points a question was worth come from Canvas or are left out — never a "/ 0" standing in for them)
      const worth = r.possible !== null ? `${store.fmtPts(r.possible)} pts` : '';
      const scoreLbl = r.earned !== null ? (r.possible !== null ? `${store.fmtPts(r.earned)} / ${store.fmtPts(r.possible)}` : `${store.fmtPts(r.earned)} ${Number(r.earned) === 1 ? 'pt' : 'pts'}`) : part ? (worth ? `Partial · ${worth}` : 'Partial') : worth;
      const sol = r.sol;
      const card = U.el(`bcv-fb__q bcv-qfb__q is-${v}`, [
        U.el('bcv-fb__qhead', [
          h('span', { class: 'bcv-fb__mark', style: { background: tint } }, U.svg(MARK[v], { size: 13, stroke: ink, width: 2.8 })),
          h('span', { class: 'bcv-fb__qn', text: `Question ${r.k + 1}` }),
          h('span', { class: 'bcv-qfb__verdict', style: { color: ink }, text: VERDICT[v] }),
          h('span', { class: 'bcv-fb__score', style: { color: ink }, text: scoreLbl }),
        ]),
        CS().prose(noFields(r.q.question_text, r.q) || r.q.question_name || '', { cls: 'bcv-fb__qtext bcv-qfb__qtext' }),
        fbAnswers(r),
        fbOptions(r),
        sol ? U.el('bcv-fb__sol', [
          U.text('bcv-fb__kicker', 'Worked solution', 'span'),
          sol.html ? CS().prose(sol.html, { cls: 'bcv-fb__solbody' }) : h('p', { class: 'bcv-fb__solbody', text: sol.text }),
        ]) : r.read ? U.text('bcv-fb__none bcv-qfb__nosol', 'Your instructor left no worked solution for this question.') : null, // (said only once Canvas's results page was read: the API never hands a student the comments)
      ]);
      card.id = `bcv-fbq-${r.k}`;
      card.dataset.k = String(r.k);
      card.dataset.verdict = v;
      card.tabIndex = -1;
      return U.enter(card, i, 45);
    }
    const toReviewAgain = (v) => v === 'wrong' || v === 'partial';
    /** The rail: the score, the filter, a square per question, and the ways out. */
    function fbRail(fb, btns) {
      const { sub, rows } = fb;
      const pct = fb.possible > 0 && fb.score !== null ? Math.round((fb.score / fb.possible) * 100) : null;
      const nRight = rows.filter((r) => r.correct === true).length;
      const when = sub.workflow_state === 'pending_review' ? 'awaiting your instructor’s review' : `graded ${U.fmtAtUpper(fb.gradedAt)}`;
      const counts = { all: rows.length, wrong: rows.filter((r) => toReviewAgain(verdictOf(r))).length, right: nRight };
      const filters = fb.released ? U.el('bcv-qfb__filters', [['all', 'All'], ['wrong', 'To review'], ['right', 'Correct']].map(([key, label]) => h('button', {
        type: 'button', class: `bcv-qfb__filter ${key === 'all' ? 'is-on' : ''}`, dataset: { filter: key }, 'aria-pressed': key === 'all' ? 'true' : 'false',
      }, [h('span', { text: label }), h('span', { class: 'bcv-qfb__count', text: String(counts[key]) })]))) : null;
      return h('aside', { class: 'bcv-qfb__rail', 'aria-label': 'Your results' }, [
        U.el('bcv-qfb__score', [
          U.text('bcv-qz__railh', `Attempt ${sub.attempt || 1}`, 'div'),
          U.el('bcv-fb__scoreline', [
            h('span', { class: 'bcv-fb__big', text: `${fb.score !== null ? store.fmtPts(fb.score) : '—'} / ${store.fmtPts(fb.possible)}` }),
            pct !== null ? h('span', { class: 'bcv-fb__pct', text: `${pct}%` }) : null,
            h('span', { class: 'bcv-fb__summary', text: `${fb.released ? `${nRight} of ${rows.length} correct · ` : ''}${when}` }),
          ]),
          U.el('bcv-fb__bar', h('div', { class: 'bcv-fb__fill', style: { width: `${pct ?? 0}%` } })),
        ]),
        filters,
        U.text('bcv-qz__railh', 'Questions', 'div'),
        U.el('bcv-qfb__nav', rows.map((r) => {
          const v = verdictOf(r);
          return h('button', { type: 'button', class: `bcv-qfb__sq is-${v}`, dataset: { k: String(r.k) }, title: `Question ${r.k + 1} · ${VERDICT[v]}`, 'aria-label': `Question ${r.k + 1}, ${VERDICT[v]}` }, h('span', { text: String(r.k + 1) }));
        })),
        U.text('bcv-qfb__keys', 'J and K go to the next and previous question.', 'div'),
        btns,
      ]);
    }
    /** The rail and the questions, and what moves between them: the squares, the filter, J and K, and
     *  the square of the question being read kept lit as the page scrolls. */
    function feedbackView(fb, btns) {
      const comments = fb.comments.map((c) => U.el('bcv-fb__comment', [
        h('span', { class: 'bcv-fb__avatar', text: U.initials(c.author_name || c.author?.display_name || '') || '·' }),
        U.el('bcv-fb__cbody', [U.text('bcv-fb__ctitle', `Instructor comment${c.author_name ? ` · ${c.author_name}` : ''}`), h('p', { class: 'bcv-fb__ctext', text: c.comment || '' })]),
      ]));
      const notice = fb.released ? null : U.hint('Canvas has not released the question results for this attempt yet — your score and the questions are shown as they stand.', 'bcv-hint--narrow');
      let n = 1;
      const main = h('div', { class: 'bcv-qfb__main' }, [
        comments.length ? U.enter(U.el('bcv-qfb__comments', comments), n++, 45) : null,
        notice,
        ...fb.rows.map((r) => fbCard(r, n++)),
        h('p', { class: 'bcv-qfb__empty', hidden: true, text: 'Nothing here — every question in this attempt was answered correctly.' }),
      ]);
      const view = U.el('bcv-qfb', [U.enter(fbRail(fb, btns), 0, 45), main]);
      const cards = () => [...main.querySelectorAll('.bcv-qfb__q')];
      const squares = () => [...view.querySelectorAll('.bcv-qfb__sq')];
      const reduce = () => !!self.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
      const line = () => (screen.querySelector('.bcv-qz__head')?.getBoundingClientRect().bottom || 0) + 14;
      const here = (k) => { for (const sq of squares()) sq.classList.toggle('is-here', sq.dataset.k === String(k)); };
      // a question gone to stays lit while the page is on its way there and once it has arrived, even
      // where the page cannot bring it up to the line (the last few of a short attempt); moving the
      // page yourself, or a trip cut short, hands the light back to whichever question is at the line
      let pin = null;
      const spy = () => {
        if (pin && main.querySelector(`#bcv-fbq-${pin.k}`)?.hidden !== false) pin = null; // (filtered away)
        if (pin) {
          const d = Math.abs(window.scrollY - pin.y);
          if (d < 3) pin.there = true;
          if (d < 3 || (!pin.there && Date.now() - pin.t < 1500)) { here(pin.k); return; }
          pin = null;
        }
        const at = line();
        const c = cards().find((x) => !x.hidden && x.getBoundingClientRect().bottom > at + 40);
        if (c) here(c.dataset.k);
      };
      function setFilter(key) {
        view.dataset.filter = key;
        for (const b of view.querySelectorAll('.bcv-qfb__filter')) { const on = b.dataset.filter === key; b.classList.toggle('is-on', on); b.setAttribute('aria-pressed', on ? 'true' : 'false'); }
        let shown = 0;
        for (const c of cards()) {
          const v = c.dataset.verdict;
          c.hidden = !(key === 'all' || (key === 'wrong' && toReviewAgain(v)) || (key === 'right' && v === 'right'));
          if (!c.hidden) shown++;
        }
        for (const sq of squares()) sq.classList.toggle('is-out', !!main.querySelector(`#bcv-fbq-${sq.dataset.k}`)?.hidden);
        const empty = main.querySelector('.bcv-qfb__empty');
        empty.hidden = shown > 0;
        empty.textContent = key === 'right' ? 'No question in this attempt was answered correctly.' : 'Nothing to review — every question in this attempt was answered correctly.';
        spy();
      }
      function goTo(k) {
        const c = main.querySelector(`#bcv-fbq-${k}`);
        if (!c) return;
        if (c.hidden) setFilter('all');
        const y = Math.round(Math.max(0, Math.min(document.documentElement.scrollHeight - window.innerHeight, window.scrollY + c.getBoundingClientRect().top - line())));
        pin = { k: String(k), y, t: Date.now(), there: Math.abs(window.scrollY - y) < 3 };
        window.scrollTo({ top: y, behavior: reduce() ? 'auto' : 'smooth' });
        here(k);
        c.focus({ preventScroll: true });
      }
      view.addEventListener('click', (e) => {
        const sq = e.target.closest('.bcv-qfb__sq');
        if (sq) { goTo(sq.dataset.k); return; }
        const f = e.target.closest('.bcv-qfb__filter');
        if (f) setFilter(f.dataset.filter);
      });
      const onKey = (e) => {
        if (e.metaKey || e.ctrlKey || e.altKey || (e.key !== 'j' && e.key !== 'k')) return;
        if (/^(INPUT|TEXTAREA|SELECT)$/.test(e.target?.tagName || '') || e.target?.isContentEditable || document.querySelector('.bcv-sheet-ov')) return;
        const vis = cards().filter((c) => !c.hidden);
        if (!vis.length) return;
        const cur = vis.findIndex((c) => squares().find((sq) => sq.dataset.k === c.dataset.k)?.classList.contains('is-here'));
        const next = vis[Math.max(0, Math.min(vis.length - 1, (cur < 0 ? -1 : cur) + (e.key === 'j' ? 1 : -1)))];
        if (!next) return;
        e.preventDefault();
        goTo(next.dataset.k);
      };
      let raf = 0;
      const onScroll = () => { if (!raf) raf = requestAnimationFrame(() => { raf = 0; spy(); }); };
      document.addEventListener('keydown', onKey);
      window.addEventListener('scroll', onScroll, { passive: true });
      U.onGone(view, () => { document.removeEventListener('keydown', onKey); window.removeEventListener('scroll', onScroll); cancelAnimationFrame(raf); });
      requestAnimationFrame(spy);
      return view;
    }
    function feedback() {
      const sub = st.fbSub;
      const wrap = U.el('bcv-qz__stage');
      const plain = (...kids) => U.el('bcv-fb', kids); // (the states before there is anything to review: one column, as before)
      const btns = () => U.el('bcv-fb__btns', [
        st.fbFrom === 'done' ? h('button', { type: 'button', class: 'bcv-qz__big', text: 'Back to receipt', onclick: () => { st.stage = 'done'; draw(); toTop(); } }) : null,
        st.fbFrom === 'intro' ? h('button', { type: 'button', class: 'bcv-qz__big', text: 'Quiz overview', onclick: () => { st.stage = 'intro'; draw(); toTop(); } }) : null,
        h('button', { type: 'button', class: 'bcv-qz__big bcv-qz__big--primary', text: /survey/.test(quiz.quiz_type || '') ? 'Back to the survey' : 'Back to the quiz', onclick: () => exitTo(simulated ? simHome : quizUrl) }),
      ]);
      if (!sub) {
        wrap.append(plain(U.emptyCard('No finished attempts yet — feedback appears here once one is submitted.'), btns()));
        return wrap;
      }
      const hidden = resultsHidden(sub);
      if (hidden) {
        wrap.append(plain(U.card(U.el('bcv-detail', [h('h2', { class: 'bcv-detail__title', text: 'Results not released' }), U.text('bcv-hint', hidden)]), 'bcv-card--22 bcv-qfb__withheld'), btns()));
        return wrap;
      }
      const heldCard = () => plain(U.card(U.el('bcv-detail', [h('h2', { class: 'bcv-detail__title', text: 'Results not released' }), U.text('bcv-hint', HELD_LINE)]), 'bcv-card--22 bcv-qfb__withheld'), btns());
      if (st.fb && st.fb.sub === sub) {
        wrap.append(st.fb.held ? heldCard() : feedbackView(st.fb, btns()));
        return wrap;
      }
      wrap.append(plain(U.loading('rows', 4)));
      loadFeedback(sub).then((fb) => {
        if (!ctx.alive() || st.stage !== 'feedback' || st.fbSub !== sub) return;
        st.fb = fb;
        wrap.replaceChildren(fb.held ? heldCard() : feedbackView(fb, btns()));
      }).catch((e) => {
        if (ctx.alive()) wrap.replaceChildren(plain(U.errorBox(`The feedback could not be loaded: ${e.message}`), btns()));
      });
      return wrap;
    }

    draw();
    return screen;
  }

  BCV.screens.quiz = { render };
})();
