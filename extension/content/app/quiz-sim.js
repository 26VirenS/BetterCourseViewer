/* The simulation quiz (Settings → Developer → Quiz): a Classic quiz with one question of every
 * kind Canvas has — multiple choice, true/false, fill in the blank, fill in multiple blanks,
 * multiple answers, multiple dropdowns, matching, numerical, formula, essay, file upload and a
 * text block — that lives in this tab alone. It stands in for Canvas's quiz API behind the same
 * calls the quiz screen makes (content/app/screens/quiz.js), so every part of taking a quiz runs
 * as it does on a real one — the pills, flags, the clock, saving, the review, submitting, the
 * receipt, the feedback, Import answers — and nothing is ever sent to Canvas.
 *
 * It answers the way Canvas's API does, refusals included: an answer of the wrong shape (an id as
 * a string where Canvas wants a whole number, a file it does not hold) is refused in Canvas's own
 * words, so a bug in what the screen sends shows here first. The questions come censored the way a
 * student gets them (no answer weights) until an attempt is over; the grading and the graded
 * history are worked out here. The attempts are kept in this tab's session storage, so a reload
 * keeps them and closing the tab ends them. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const KEY = 'bcv:quizSim';
  const ID = '0'; // the quiz's id in the address: /courses/<a course>/quizzes/0?bcv=take&sim=1
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));
  const LAG = 90; // (a network's worth of delay: the loading states show as they would)

  const Q = [
    { id: 1001, question_type: 'text_only_question', points_possible: 0, question_text: '<p>A simulation: one question of every Classic kind, answered, saved and marked in this browser. <strong>Nothing is sent to Canvas.</strong></p>', answers: [] },
    { id: 1002, question_type: 'multiple_choice_question', points_possible: 2, question_text: '<p>Which planet is closest to the Sun?</p>', answers: [{ id: 10021, text: 'Mercury', weight: 100 }, { id: 10022, text: 'Venus', weight: 0 }, { id: 10023, text: 'Earth', weight: 0 }, { id: 10024, text: 'Mars', weight: 0 }], neutral_comments: 'Mercury orbits at about 0.39 AU.' },
    { id: 1003, question_type: 'true_false_question', points_possible: 1, question_text: '<p>Light travels faster than sound.</p>', answers: [{ id: 10031, text: 'True', weight: 100 }, { id: 10032, text: 'False', weight: 0 }] },
    { id: 1004, question_type: 'short_answer_question', points_possible: 2, question_text: '<p>The chemical symbol for gold is ______.</p>', answers: [{ id: 10041, text: 'Au', weight: 100 }] },
    { id: 1005, question_type: 'fill_in_multiple_blanks_question', points_possible: 2, question_text: '<p>Roses are [colour1], violets are [colour2].</p>', answers: [{ id: 10051, text: 'red', blank_id: 'colour1', weight: 100 }, { id: 10052, text: 'blue', blank_id: 'colour2', weight: 100 }] },
    { id: 1006, question_type: 'multiple_answers_question', points_possible: 3, question_text: '<p>Which of these are prime? Pick every one.</p>', answers: [{ id: 10061, text: '2', weight: 100 }, { id: 10062, text: '4', weight: 0 }, { id: 10063, text: '7', weight: 100 }, { id: 10064, text: '9', weight: 0 }, { id: 10065, text: '11', weight: 100 }] },
    { id: 1007, question_type: 'multiple_dropdowns_question', points_possible: 2, question_text: '<p>The [animal] says [sound].</p>', answers: [{ id: 10071, text: 'cow', blank_id: 'animal', weight: 100 }, { id: 10072, text: 'cat', blank_id: 'animal', weight: 0 }, { id: 10073, text: 'moo', blank_id: 'sound', weight: 100 }, { id: 10074, text: 'meow', blank_id: 'sound', weight: 0 }] },
    // (a match id of 0 on purpose: the falsy one, which the sending once turned back into a string)
    { id: 1008, question_type: 'matching_question', points_possible: 3, question_text: '<p>Match each country to its capital.</p>', answers: [{ id: 10081, text: 'France', match_id: 0 }, { id: 10082, text: 'Japan', match_id: 802 }, { id: 10083, text: 'Kenya', match_id: 803 }], matches: [{ match_id: 0, text: 'Paris' }, { match_id: 802, text: 'Tokyo' }, { match_id: 803, text: 'Nairobi' }, { match_id: 804, text: 'Lima' }] },
    { id: 1009, question_type: 'numerical_question', points_possible: 2, question_text: '<p>What is 7 × 8?</p>', answers: [{ id: 10091, numerical_answer_type: 'exact_answer', exact: 56, margin: 0, weight: 100 }] },
    // a formula question: its variables already put in the text for this attempt, as Canvas does
    { id: 1010, question_type: 'calculated_question', points_possible: 2, question_text: '<p>A car travels 150 km in 2.5 h. What is its average speed in km/h?</p>', answer_tolerance: 0.5, formula_decimal_places: 0, answers: [{ id: 10101, answer: 60, weight: 100 }] },
    { id: 1011, question_type: 'essay_question', points_possible: 5, question_text: '<p>In a few sentences, say how you would test a quiz.</p>', answers: [] },
    { id: 1012, question_type: 'file_upload_question', points_possible: 3, question_text: '<p>Upload any file: it stays in this browser.</p>', answers: [] },
  ].map((q, k) => ({ ...q, position: k + 1, question_name: `Question ${k + 1}` }));
  const byId = new Map(Q.map((q) => [String(q.id), q]));
  const HAND = new Set(['essay_question', 'file_upload_question']); // marked by hand: nothing earned until then
  const BLANKS = new Set(['fill_in_multiple_blanks_question', 'multiple_dropdowns_question']);

  // ---- the attempts, kept for this tab ---------------------------------------------------------
  let db = null;
  function load() {
    if (db) return db;
    try { db = JSON.parse(sessionStorage.getItem(KEY) || 'null'); } catch { db = null; }
    if (!db || !Array.isArray(db.subs)) db = { subs: [], files: {}, seq: 0 };
    return db;
  }
  function keep() { try { sessionStorage.setItem(KEY, JSON.stringify(db)); } catch { /* this visit only, then */ } }
  const nextId = () => { load(); db.seq += 1; return db.seq; };
  const subOf = (sub) => load().subs.find((s) => String(s.id) === String(sub?.id));
  const refuse = (message) => { throw Object.assign(new Error(message), { status: 400 }); };
  const isWhole = (v) => typeof v === 'number' && Number.isInteger(v);
  const nowIso = () => new Date().toISOString();

  // ---- the quiz, as Canvas's API describes it to a student -------------------------------------
  const quiz = () => ({
    id: ID, title: 'Simulation quiz', quiz_type: 'assignment', assignment_id: 'sim',
    description: '<p>Every Classic question kind in one quiz, for trying the quiz screen out. It lives in this browser tab: nothing is sent to Canvas, and closing the tab ends it.</p><ul><li>The formula question asks for a whole number.</li><li>The essay and the file are marked by hand, so they earn nothing until then.</li></ul>',
    question_count: Q.length, points_possible: Q.reduce((s, q) => s + q.points_possible, 0),
    time_limit: 30, allowed_attempts: -1, one_question_at_a_time: false, cant_go_back: false,
    show_correct_answers: true, hide_results: null, shuffle_answers: false, due_at: null,
    locked_for_user: false, has_access_code: false, ip_filter: null, require_lockdown_browser: false,
  });

  // what a student's answer looks like coming back: a matching answer lists every left-hand value (a
  // null match where none is set) and a blank kind every blank (null where empty), as Canvas's does
  function canvasAnswer(q, a) {
    if (q.question_type === 'matching_question') {
      const got = new Map((Array.isArray(a) ? a : []).map((p) => [String(p.answer_id), p.match_id]));
      return q.answers.map((x) => ({ answer_id: String(x.id), match_id: got.has(String(x.id)) ? String(got.get(String(x.id))) : null }));
    }
    if (BLANKS.has(q.question_type)) {
      const held = a && typeof a === 'object' && !Array.isArray(a) ? a : {};
      return Object.fromEntries([...new Set(q.answers.map((x) => x.blank_id))].map((b) => [b, held[b] === undefined || held[b] === null || held[b] === '' ? null : String(held[b])]));
    }
    return a ?? null;
  }
  /** A question as the student gets it: no answer weights, no comments — until the attempt is over. */
  function shown(q, s) {
    const over = s.workflow_state !== 'untaken';
    const answers = q.answers.map((x) => {
      const { weight, exact, margin, answer, ...rest } = x; // (a formula's result and a number's range are the answer: kept back)
      return over ? { ...rest, weight, ...(exact !== undefined ? { exact, margin } : {}), ...(answer !== undefined ? { answer } : {}) } : rest;
    });
    const out = { id: String(q.id), position: q.position, question_name: q.question_name, question_type: q.question_type, question_text: q.question_text, points_possible: q.points_possible, answers, flagged: !!s.flags[q.id], answer: canvasAnswer(q, s.answers[q.id]) };
    if (q.matches) out.matches = q.matches.map((m) => ({ ...m }));
    if (q.formula_decimal_places !== undefined) out.formula_decimal_places = q.formula_decimal_places;
    if (over) {
      out.correct = mark(q, s.answers[q.id]).correct;
      if (q.neutral_comments) out.neutral_comments = q.neutral_comments;
    }
    return out;
  }

  // ---- marking ---------------------------------------------------------------------------------
  const norm = (t) => String(t ?? '').trim().toLowerCase();
  /** { correct: true | false | 'partial' | null (by hand), points } for one answer. */
  function mark(q, a) {
    const pts = q.points_possible;
    const none = a === null || a === undefined || a === '' || (Array.isArray(a) && !a.length) || (typeof a === 'object' && !Array.isArray(a) && !Object.keys(a).length);
    if (q.question_type === 'text_only_question') return { correct: null, points: 0 };
    if (HAND.has(q.question_type)) return { correct: null, points: 0 };
    if (none) return { correct: false, points: 0 };
    const right = q.answers.filter((x) => x.weight === 100);
    switch (q.question_type) {
      case 'multiple_choice_question':
      case 'true_false_question': { const ok = right.some((x) => x.id === Number(a)); return { correct: ok, points: ok ? pts : 0 }; }
      case 'short_answer_question': { const ok = right.some((x) => norm(x.text) === norm(a)); return { correct: ok, points: ok ? pts : 0 }; }
      case 'numerical_question': { const x = right[0]; const ok = Math.abs(Number(a) - x.exact) <= (x.margin || 0); return { correct: ok, points: ok ? pts : 0 }; }
      case 'calculated_question': { const ok = Math.abs(Number(a) - right[0].answer) <= (q.answer_tolerance || 0); return { correct: ok, points: ok ? pts : 0 }; }
      case 'multiple_answers_question': {
        const want = new Set(right.map((x) => x.id));
        const got = new Set((Array.isArray(a) ? a : [a]).map(Number));
        const hits = [...got].filter((id) => want.has(id)).length;
        const wrong = [...got].filter((id) => !want.has(id)).length;
        const share = Math.max(0, hits - wrong) / want.size; // (Canvas's own rule: a wrong pick takes one right one back)
        return { correct: share === 1 && !wrong ? true : share > 0 ? 'partial' : false, points: Math.round(pts * share * 100) / 100 };
      }
      case 'matching_question': {
        const got = new Map((Array.isArray(a) ? a : []).map((p) => [String(p.answer_id), String(p.match_id)]));
        const hits = q.answers.filter((x) => got.get(String(x.id)) === String(x.match_id)).length;
        const share = hits / q.answers.length;
        return { correct: share === 1 ? true : share > 0 ? 'partial' : false, points: Math.round(pts * share * 100) / 100 };
      }
      case 'fill_in_multiple_blanks_question':
      case 'multiple_dropdowns_question': {
        const drops = q.question_type === 'multiple_dropdowns_question';
        const blanks = [...new Set(q.answers.map((x) => x.blank_id))];
        const hits = blanks.filter((b) => right.some((x) => x.blank_id === b && (drops ? String(x.id) === String(a[b]) : norm(x.text) === norm(a[b])))).length;
        const share = hits / blanks.length;
        return { correct: share === 1 ? true : share > 0 ? 'partial' : false, points: Math.round(pts * share * 100) / 100 };
      }
      default: return { correct: null, points: 0 };
    }
  }
  /** The graded history's fields for one answer, the way Canvas records them (answer_id, answer_<id>, answer_for_<blank>, text, attachment_ids). */
  function histFields(q, a) {
    if (q.question_type === 'matching_question') {
      const got = new Map((Array.isArray(a) ? a : []).map((p) => [String(p.answer_id), p.match_id]));
      return Object.fromEntries(q.answers.map((x) => [`answer_${x.id}`, got.has(String(x.id)) ? String(got.get(String(x.id))) : '']));
    }
    if (BLANKS.has(q.question_type)) {
      const held = a && typeof a === 'object' && !Array.isArray(a) ? a : {};
      const drops = q.question_type === 'multiple_dropdowns_question';
      return Object.fromEntries([...new Set(q.answers.map((x) => x.blank_id))].flatMap((b) => [[`answer_for_${b}`, held[b] === undefined || held[b] === null ? '' : String(held[b])], ...(drops ? [[`answer_id_for_${b}`, held[b] === undefined || held[b] === null ? null : Number(held[b])]] : [])]));
    }
    if (a === null || a === undefined || a === '') return {};
    if (q.question_type === 'multiple_answers_question') return Object.fromEntries((Array.isArray(a) ? a : [a]).map((id) => [`answer_${id}`, '1']));
    if (q.question_type === 'file_upload_question') return { attachment_ids: (Array.isArray(a) ? a : [a]).map(String) };
    if (q.question_type === 'multiple_choice_question' || q.question_type === 'true_false_question') return { answer_id: Number(a), text: String(a) };
    return { text: String(a) };
  }

  // ---- what Canvas refuses ---------------------------------------------------------------------
  /** The answer as Canvas would keep it, or a refusal in Canvas's words. */
  function accept(q, a) {
    const t = q.question_type;
    if (a === null || a === undefined || a === '') return null; // (cleared)
    if (t === 'text_only_question') refuse('This question takes no answer.');
    if (t === 'multiple_choice_question' || t === 'true_false_question') {
      if (!isWhole(a)) refuse('answer must be of type Integer');
      if (!q.answers.some((x) => x.id === a)) refuse('Unknown answer');
      return a;
    }
    if (t === 'multiple_answers_question') {
      if (!Array.isArray(a) || a.some((x) => !isWhole(x))) refuse('answer must be an array of Integers');
      if (a.some((x) => !q.answers.some((y) => y.id === x))) refuse('Unknown answer');
      return [...a];
    }
    if (t === 'matching_question') {
      if (!Array.isArray(a)) refuse('answer must be of type Array');
      for (const p of a) {
        if (!p || !isWhole(p.answer_id)) refuse('answer_id must be of type Integer');
        if (!isWhole(p.match_id)) refuse('match_id must be of type Integer');
        if (!q.answers.some((x) => x.id === p.answer_id) || !q.matches.some((m) => m.match_id === p.match_id)) refuse('Unknown answer');
      }
      return a.map((p) => ({ answer_id: p.answer_id, match_id: p.match_id }));
    }
    if (BLANKS.has(t)) {
      if (typeof a !== 'object' || Array.isArray(a)) refuse('answer must be of type Hash');
      const blanks = new Set(q.answers.map((x) => x.blank_id));
      for (const [b, v] of Object.entries(a)) {
        if (!blanks.has(b)) refuse(`Unknown blank ${b}`);
        if (t === 'multiple_dropdowns_question' && (!isWhole(v) || !q.answers.some((x) => x.blank_id === b && x.id === v))) refuse(`answer_id for ${b} must be one of the blank's answers`);
      }
      return { ...a };
    }
    if (t === 'numerical_question' || t === 'calculated_question') {
      if (!Number.isFinite(Number(a))) refuse('answer must be a number');
      return Number(a);
    }
    if (t === 'file_upload_question') {
      if (!Array.isArray(a)) refuse('answer must be an array of attachment ids');
      if (a.some((id) => !load().files[String(id)])) refuse('invalid attachment');
      return a.map(String);
    }
    return String(a); // short answer, essay
  }

  // ---- the calls the quiz screen makes ---------------------------------------------------------
  const quizApi = {
    async start() {
      await wait(LAG);
      load();
      const open = db.subs.find((s) => s.workflow_state === 'untaken');
      if (open) return { ...open };
      const attempt = db.subs.reduce((m, s) => Math.max(m, s.attempt), 0) + 1;
      const started = Date.now();
      const s = { id: `sim-${nextId()}`, quiz_id: ID, attempt, workflow_state: 'untaken', started_at: new Date(started).toISOString(), end_at: new Date(started + quiz().time_limit * 60e3).toISOString(), finished_at: null, score: null, kept_score: null, validation_token: `sim-token-${attempt}`, answers: {}, flags: {} };
      db.subs.push(s);
      keep();
      return { ...s };
    },
    async questions(sub) {
      await wait(LAG);
      const s = subOf(sub);
      if (!s) refuse('The attempt could not be found.');
      return Q.map((q) => shown(q, s));
    },
    async answer(sub, questionId, answer) {
      await wait(LAG);
      const s = subOf(sub);
      if (!s) refuse('The attempt could not be found.');
      if (s.workflow_state !== 'untaken') refuse('This attempt is over.');
      if (s.validation_token !== sub.validation_token || Number(s.attempt) !== Number(sub.attempt)) refuse('invalid validation token');
      const q = byId.get(String(questionId));
      if (!q) refuse('Unknown question');
      const kept = accept(q, answer);
      if (kept === null) delete s.answers[q.id]; else s.answers[q.id] = kept;
      keep();
      return { quiz_submission_questions: [shown(q, s)] };
    },
    /** Several answers in one save: every one is checked before any is kept, as one request. */
    async answerMany(sub, items) {
      await wait(LAG);
      const s = subOf(sub);
      if (!s) refuse('The attempt could not be found.');
      if (s.workflow_state !== 'untaken') refuse('This attempt is over.');
      if (s.validation_token !== sub.validation_token || Number(s.attempt) !== Number(sub.attempt)) refuse('invalid validation token');
      const kept = items.map(({ id, answer }) => {
        const q = byId.get(String(id));
        if (!q) refuse('Unknown question');
        return [q, accept(q, answer)];
      });
      for (const [q, a] of kept) { if (a === null) delete s.answers[q.id]; else s.answers[q.id] = a; }
      keep();
      return { quiz_submission_questions: kept.map(([q]) => shown(q, s)) };
    },
    async flag(sub, questionId, on) {
      await wait(LAG);
      const s = subOf(sub);
      if (!s) refuse('The attempt could not be found.');
      if (on) s.flags[questionId] = true; else delete s.flags[questionId];
      keep();
      return { quiz_submission_questions: [shown(byId.get(String(questionId)), s)] };
    },
    /** A file for the file question: kept by name and size in this tab, the three steps played as progress. */
    async uploadFile(courseId, quizId, file, onProgress) {
      for (const p of [0.25, 0.6, 1]) { await wait(LAG); onProgress?.(p); }
      const id = String(7000000 + nextId()); // (a whole number, as Canvas's ids are)
      db.files[id] = { name: file?.name || 'file', size: file?.size || 0 };
      keep();
      return id;
    },
    async complete(courseId, quizId, sub) {
      await wait(LAG * 2);
      const s = subOf(sub);
      if (!s) refuse('The attempt could not be found.');
      if (s.workflow_state !== 'untaken') refuse('This attempt is over.');
      const marks = Q.map((q) => mark(q, s.answers[q.id]));
      s.score = Math.round(marks.reduce((t, m) => t + m.points, 0) * 100) / 100;
      s.kept_score = Math.max(s.score, ...db.subs.filter((x) => x !== s && x.workflow_state !== 'untaken').map((x) => x.score || 0));
      s.finished_at = nowIso();
      // (on Canvas an answered essay holds the attempt in pending_review, score and feedback withheld until
      // it is marked; here it is complete at once, so the receipt and the feedback can be tried — the
      // essay and the file stay unmarked, worth nothing)
      s.workflow_state = 'complete';
      keep();
      return { ...s };
    },
    async time(courseId, quizId, sub) { const s = subOf(sub); return { end_at: s?.end_at || null, time_left: s?.end_at ? Math.round((Date.parse(s.end_at) - Date.now()) / 1000) : null }; },
  };

  /** The store the quiz screen uses, with the quiz's own calls answered here and everything else
   *  (preferences, points formatting, the account) left to the real one. */
  function store(base) {
    const sim = Object.create(base);
    return Object.assign(sim, {
      quiz: async () => { await wait(LAG); return quiz(); },
      quizSubmissions: async () => { await wait(LAG); return load().subs.map((s) => ({ ...s })).sort((a, b) => b.attempt - a.attempt); },
      // the assignment's submission: the graded history, attempt by attempt, that the feedback and Import answers read
      submission: async () => {
        await wait(LAG);
        const done = load().subs.filter((s) => s.workflow_state !== 'untaken').sort((a, b) => a.attempt - b.attempt);
        const last = done[done.length - 1];
        return {
          id: 'sim', assignment_id: 'sim', attempt: last?.attempt ?? null, score: last?.kept_score ?? null, workflow_state: last ? 'graded' : 'unsubmitted', graded_at: last?.finished_at || null, submission_comments: [],
          submission_history: done.map((s) => ({ attempt: s.attempt, score: s.score, submission_data: Q.filter((q) => q.question_type !== 'text_only_question').map((q) => { const m = mark(q, s.answers[q.id]); return { question_id: q.id, correct: m.correct === null ? 'undefined' : m.correct, points: m.points, ...histFields(q, s.answers[q.id]) }; }) })),
        };
      },
      quizApi,
    });
  }

  /** Every attempt forgotten: the quiz as new. */
  function reset() { db = { subs: [], files: {}, seq: 0 }; keep(); }

  BCV.quizSim = { ID, store, reset, here: (route) => route?.params?.get('sim') === '1' && String(route.arg) === ID };
})();
