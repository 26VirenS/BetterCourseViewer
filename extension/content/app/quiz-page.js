/* Canvas's own quiz-taking page, read as a source of questions.
 *
 * A quiz set to one question at a time cannot list its questions through the API: Canvas refuses
 * ("Cannot receive one question at a time questions in the API"). Its own page, though, shows
 * exactly one question per request, and the answering, flagging, timing and completion APIs all
 * still work for that attempt. So for those quizzes the attempt runs in the interface like any
 * other, with each question read from that page as Canvas serves it, and every move (Next,
 * Previous) made the way Canvas's page makes it: a form post to its record-answer action, which
 * marks the question read and lands on the next one — which is also how Canvas enforces "no
 * going back" (a question marked read takes no more answers, and the page only ever shows the
 * first unread one).
 *
 * Nothing here is invented: the markup is Canvas's (display_question / multi_answer /
 * single_answer partials, the question list in the right column, the hidden fields and the
 * next/previous buttons' data-action of take_quiz.html.erb). */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const TYPES = ['multiple_choice_question', 'true_false_question', 'multiple_answers_question', 'short_answer_question', 'numerical_question', 'calculated_question', 'essay_question', 'matching_question', 'multiple_dropdowns_question', 'fill_in_multiple_blanks_question', 'file_upload_question', 'text_only_question'];
  const NUMERIC = new Set(['numerical_question', 'calculated_question']);
  const num = (s) => {
    const n = Number(String(s ?? '').replace(/[^\d.eE+-]/g, ''));
    return String(s ?? '').trim() !== '' && Number.isFinite(n) ? n : null;
  };
  const idOf = (el, prefix) => (el && el.id && el.id.startsWith(prefix) ? el.id.slice(prefix.length) : null);
  const clean = (el) => {
    const c = el.cloneNode(true);
    c.querySelectorAll('.screenreader-only').forEach((x) => x.remove());
    return c.textContent.replace(/\s+/g, ' ').trim();
  };
  /** An answer's label: Canvas's `.answer_label` beside the input (rich HTML kept only when it is more than text). */
  const labelOf = (inp) => {
    const lbl = inp.closest('.answer')?.querySelector('.answer_label') || (inp.id ? inp.ownerDocument.getElementById(`${inp.id}_label`) : null);
    if (!lbl) return { text: '' };
    const html = lbl.innerHTML.trim();
    const text = lbl.textContent.replace(/\s+/g, ' ').trim();
    return /<[a-z][\s\S]*>/i.test(html) && !/^<(?:p|span|div)>[^<]*<\/(?:p|span|div)>$/i.test(html) ? { text, html } : { text };
  };

  /** One `.display_question` block as the question shape the quiz screen draws (the API's shape). */
  function parseQuestion(el) {
    const id = idOf(el, 'question_');
    if (!id || !/^\d+$/.test(id)) return null;
    const type = el.querySelector('.question_type')?.textContent.trim() || TYPES.find((t) => el.classList.contains(t)) || 'unknown_question';
    const textEl = el.querySelector('.question_text');
    const q = {
      id, question_name: el.querySelector('.question_name')?.textContent.trim() || '', question_type: type,
      points_possible: num(el.querySelector('.question_points_holder .points')?.textContent) ?? 0,
      question_text: textEl ? textEl.innerHTML.trim() : '', flagged: el.classList.contains('marked'), answers: [], answer: null, loaded: true,
    };
    const scope = el.querySelector('.answers') || el;
    const radios = [...scope.querySelectorAll(`input[type="radio"][name="question_${id}"]`)];
    const boxes = [...scope.querySelectorAll(`input[type="checkbox"][name^="question_${id}_answer_"]`)];
    if (radios.length) {
      q.answers = radios.map((inp) => ({ id: inp.value, ...labelOf(inp) }));
      const on = radios.find((inp) => inp.checked);
      q.answer = on ? (num(on.value) ?? on.value) : null;
    } else if (boxes.length) {
      const aid = (inp) => inp.name.slice(`question_${id}_answer_`.length);
      q.answers = boxes.map((inp) => ({ id: aid(inp), ...labelOf(inp) }));
      q.answer = boxes.filter((inp) => inp.checked).map((inp) => num(aid(inp)) ?? aid(inp));
    } else {
      const field = el.querySelector(`textarea[name="question_${id}"], input[type="text"][name="question_${id}"]`);
      if (field) {
        const v = (field.value || '').trim();
        q.answer = v === '' ? null : (NUMERIC.has(type) && num(v) !== null ? num(v) : v);
      }
    }
    return q;
  }

  /** The page as a whole: the attempt's form (its fields and the actions of its buttons), the clock, the
   *  question list from the right column and the question(s) shown. `ok` is false when Canvas showed
   *  something else (the quiz page, an access-code prompt, a sign-in). */
  function parse(html) {
    const doc = new DOMParser().parseFromString(html, 'text/html');
    const form = doc.querySelector('#submit_quiz_form');
    if (!form) return { ok: false, accessCode: !!doc.querySelector('#quiz_access_code, input[name="access_code"]'), signIn: !!doc.querySelector('#login_form, form[action*="/login"]'), title: (doc.title || '').trim() };
    const val = (sel) => form.querySelector(sel)?.value ?? null;
    const action = (sel) => form.querySelector(sel)?.getAttribute('data-action') || null;
    const list = [...doc.querySelectorAll('#question_list .list_question')].map((li) => ({
      id: idOf(li, 'list_question_'), name: clean(li),
      answered: li.classList.contains('answered'), flagged: li.classList.contains('marked'), seen: li.classList.contains('seen'),
      current: li.classList.contains('current_question'), textOnly: li.classList.contains('text_only'),
      href: li.querySelector('a[href]')?.getAttribute('href') || null,
    })).filter((e) => e.id);
    const questions = [...form.querySelectorAll('.display_question.question')].map(parseQuestion).filter(Boolean);
    const urls = doc.querySelector('#quiz_urls');
    return {
      ok: true,
      form: {
        attempt: num(val('input[name="attempt"]')), validationToken: val('input[name="validation_token"]'), lastQuestionId: val('input[name="last_question_id"]'),
        nextAction: action('.next-question'), prevAction: action('.previous-question'), submitAction: action('#submit_quiz_button') || form.getAttribute('action') || null,
        oneAtATime: form.classList.contains('one_question_at_a_time'), cantGoBack: form.classList.contains('cant_go_back'), lastPage: form.classList.contains('last_page'),
      },
      endAt: urls?.querySelector('.end_at')?.textContent.trim() || null,
      timeLeft: num(urls?.querySelector('.time_left')?.textContent),
      list, questions,
    };
  }

  const abs = (path) => new URL(path, location.origin).toString();
  /** The take page: the attempt's current question (Canvas decides which when going back is off), or one question by id. */
  async function fetchPage(quizUrl, { questionId = null, accessCode = null } = {}) {
    const u = new URL(questionId ? `${quizUrl}/take/questions/${encodeURIComponent(questionId)}` : `${quizUrl}/take`, location.origin);
    if (accessCode) u.searchParams.set('access_code', accessCode);
    const res = await fetch(u.toString(), { credentials: 'same-origin', headers: { accept: 'text/html' }, cache: 'no-store' });
    if (res.status === 401 || res.status === 403) throw new Error('Canvas would not show the quiz page.');
    const pg = parse(await res.text());
    pg.url = res.url;
    return pg;
  }
  /** A move the way Canvas's page makes it: its record-answer action, posted with the form's fields; the
   *  response (after Canvas's redirect) is the next page. */
  async function advance(action, fields) {
    const body = new URLSearchParams();
    for (const [k, v] of Object.entries(fields)) if (v !== null && v !== undefined) body.set(k, String(v));
    const res = await fetch(abs(action), { method: 'POST', credentials: 'same-origin', headers: { 'content-type': 'application/x-www-form-urlencoded', accept: 'text/html' }, body: body.toString(), cache: 'no-store' });
    if (res.status === 401 || res.status === 403) throw new Error('Canvas would not accept the move.');
    const pg = parse(await res.text());
    pg.url = res.url;
    return pg;
  }
  /** Why a page did not carry a question, as a message. */
  const notShown = (pg) => new Error(pg.accessCode ? 'Canvas asks for the access code before showing this quiz.' : pg.signIn ? 'Canvas asks you to sign in again.' : 'Canvas did not show the question — open the quiz page to check the attempt.');

  BCV.quizPage = { parse, fetchPage, advance, notShown };
})();
