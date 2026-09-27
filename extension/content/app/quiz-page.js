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
  /** The value a matching dropdown is set against: its row's text, less whatever the dropdown says. */
  function leftOf(sel) {
    const row = sel.closest('.answer_match, .answer, tr, li, div');
    if (!row) return '';
    const own = sel.selectedOptions?.[0]?.textContent || '';
    const left = row.querySelector('.answer_match_left, .answer_match_left_html');
    const text = (left ? left.textContent : row.textContent.replace(own, '')).replace(/\s+/g, ' ').trim();
    return text;
  }

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
    } else if (type === 'matching_question') {
      // Canvas draws each left-hand value with a dropdown of the same right-hand list beside it. The
      // list is read off the first dropdown (every one carries it), and the left-hand text off the row.
      const sels = [...scope.querySelectorAll(`select[name^="question_${id}_answer_"]`)];
      if (sels.length) {
        q.matches = [...sels[0].options].filter((o) => o.value).map((o) => ({ match_id: num(o.value) ?? o.value, text: o.textContent.trim() }));
        q.answers = sels.map((sel) => ({ id: sel.name.slice(`question_${id}_answer_`.length), text: leftOf(sel) }));
        const on = sels.filter((sel) => sel.value);
        q.answer = on.length ? on.map((sel) => ({ answer_id: num(sel.name.slice(`question_${id}_answer_`.length)) ?? sel.name.slice(`question_${id}_answer_`.length), match_id: num(sel.value) ?? sel.value })) : null;
      }
    } else if (type === 'multiple_dropdowns_question' || type === 'fill_in_multiple_blanks_question') {
      // One field per blank, named for the blank it fills; a dropdown carries that blank's own list.
      // Canvas writes these into the sentence itself, not into the answers block, so the whole
      // question is searched rather than only the block the other kinds keep their answers in.
      const fields = [...el.querySelectorAll(`select[name^="question_${id}_"], input[type="text"][name^="question_${id}_"]`)]
        .filter((f) => !f.name.startsWith(`question_${id}_answer_`));
      const blankOf = (f) => f.name.slice(`question_${id}_`.length);
      if (fields.length) {
        q.blanks = fields.map(blankOf);
        q.answers = fields.flatMap((f) => (f.tagName === 'SELECT'
          ? [...f.options].filter((o) => o.value).map((o) => ({ id: num(o.value) ?? o.value, text: o.textContent.trim(), blank_id: blankOf(f) }))
          : []));
        const filled = fields.filter((f) => String(f.value || '').trim());
        q.answer = filled.length ? Object.fromEntries(filled.map((f) => [blankOf(f), f.tagName === 'SELECT' ? (num(f.value) ?? f.value) : f.value.trim()])) : null;
      }
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

  /* ---- the results page ---------------------------------------------------------------------
   * Canvas's question API censors what it hands a student (Api::V1::QuizQuestion#censor): no points
   * per question, no answer weights, no comments — ever, finished attempt or not. Its own results
   * page (quizzes/:id/history, display_question / display_answer) is where a student sees them, so
   * a finished attempt's are read from there: each question's points ("2 / 4 pts"), the answers it
   * marks correct and the rows of a matching question it marks right or wrong (drawn only where the
   * quiz shows correct answers — Canvas decides, by drawing the mark or not), and the comment it
   * shows for how the question went. */
  /** A score as Canvas writes one: "4", "0.5", "1,000", or "2,5" where the decimal mark is a comma. */
  const score = (s) => {
    let t = String(s ?? '').replace(/[^\d.,-]/g, '');
    if (!/\d/.test(t)) return null;
    t = /^-?\d{1,3}(,\d{3})+(\.\d+)?$/.test(t) ? t.replace(/,/g, '') : t.includes('.') ? t.replace(/,/g, '') : t.replace(',', '.');
    const n = Number(t);
    return Number.isFinite(n) ? n : null;
  };
  const shownText = (el) => (el ? el.textContent.replace(/\s+/g, ' ').trim() : '');
  /** An answer as the page draws it: its words or its rich content, or a numerical answer's value. */
  function answerOf(a) {
    const html = a.querySelector(':scope .answer_html')?.innerHTML.trim() || '';
    const text = shownText(a.querySelector(':scope .answer_text'));
    const kind = shownText(a.querySelector('.numerical_answer_type'));
    if (kind && !text && !html) {
      const exact = shownText(a.querySelector('.answer_exact'));
      const margin = score(a.querySelector('.answer_error_margin')?.textContent);
      if (kind === 'range_answer' && a.querySelector('.answer_range_start')) return { text: `${shownText(a.querySelector('.answer_range_start'))} – ${shownText(a.querySelector('.answer_range_end'))}`, html: '' };
      if (kind === 'precision_answer' && a.querySelector('.answer_approximate')) return { text: shownText(a.querySelector('.answer_approximate')), html: '' };
      if (exact) return { text: margin ? `${exact} ± ${shownText(a.querySelector('.answer_error_margin'))}` : exact, html: '' };
    }
    if (html && (/<(img|math|sup|sub|table)\b/i.test(html) || !text)) return { text: '', html }; // (a formula is Canvas's own image: kept as the page has it)
    return { text, html: '' };
  }
  function parseResults(html) {
    const doc = new DOMParser().parseFromString(html, 'text/html');
    const out = new Map();
    for (const el of doc.querySelectorAll('.display_question.question[id^="question_"]')) {
      const id = idOf(el, 'question_');
      if (!/^\d+$/.test(id || '')) continue;
      const pts = el.querySelector('.question_points_holder .user_points .question_points') || el.querySelector('.question_points_holder .question_points');
      const rows = [...el.querySelectorAll('.answer[id^="answer_"]')].filter((a) => /^answer_\d+$/.test(a.id));
      const right = rows.filter((a) => a.classList.contains('correct_answer')).map((a) => ({ id: a.id.slice(7), blank: [...a.classList].find((c) => c.startsWith('answer_for_') && c.length > 11)?.slice(11) || null, ...answerOf(a) }));
      // a matching question: each row marked right or wrong, a wrong one followed by the match that was right
      const match = el.classList.contains('matching_question') ? new Map(rows.map((a) => {
        const ok = a.classList.contains('correct_answer') ? true : a.classList.contains('wrong_answer') ? false : null;
        const next = a.nextElementSibling;
        const was = ok === false && next && !next.id ? shownText(next.querySelector('.correct_answer .answer_text')) : '';
        return [a.id.slice(7), { ok, right: was || null }];
      })) : null;
      // the comment Canvas shows for how it went (QuizzesHelper#question_comment): its paragraphs, the empty ones dropped
      const box = [...el.querySelectorAll('.quiz_comment')].find((c) => c.querySelector('.correct_comments, .incorrect_comments, .neutral_comments') && !c.closest('.answer, .question_comments'));
      let comment = null;
      if (box) {
        const c = box.cloneNode(true);
        c.querySelectorAll('p').forEach((p) => { if (!p.textContent.trim() && !p.querySelector('img, math, iframe, video')) p.remove(); });
        comment = c.innerHTML.trim() || null;
      }
      out.set(id, { possible: score(String(pts?.textContent || '').split('/').pop()), shown: !!el.querySelector('.answer.correct_answer, .answer.wrong_answer'), right, match, comment });
    }
    return out;
  }
  /** One attempt's results page, read (null when Canvas shows none: results hidden, a lockdown browser
   *  wanted). Which attempt the page opens on is Canvas's choice (the current one, or the first finished
   *  one while another is in progress), so the page is asked for plainly and its attempt list read: the
   *  one it shows is marked selected, each link says its attempt ("Attempt 2: 8"), in attempt order —
   *  and if the one shown is not the one wanted, its link (a version) is followed. */
  async function results(courseId, quizId, sub) {
    const base = abs(`/courses/${courseId}/quizzes/${quizId}/history`);
    const get = async (params) => {
      const u = new URL(base);
      for (const [k, v] of Object.entries(params)) u.searchParams.set(k, String(v));
      const res = await fetch(u.toString(), { credentials: 'same-origin', headers: { accept: 'text/html' }, cache: 'no-store' });
      if (!res.ok) throw new Error(`Canvas would not show the results (${res.status}).`);
      return res.text();
    };
    let html = await get({ quiz_submission_id: sub.id });
    const doc = new DOMParser().parseFromString(html, 'text/html');
    const listed = [...doc.querySelectorAll('#quiz_versions li')].map((li, i) => {
      const a = li.querySelector('a[href*="version="]');
      return { attempt: Number((/(\d+)\s*:/.exec(a?.textContent || '') || [])[1]) || i + 1, selected: li.classList.contains('selected'), version: a ? new URL(a.getAttribute('href'), base).searchParams.get('version') : null };
    });
    const want = Number(sub.attempt);
    const shown = listed.find((x) => x.selected);
    if (listed.length && want && (!shown || shown.attempt !== want)) {
      const v = listed.find((x) => x.attempt === want)?.version;
      if (!v) return null; // (Canvas lists no such attempt: nothing is read rather than another attempt's results)
      html = await get({ quiz_submission_id: sub.id, version: v });
    }
    const map = parseResults(html);
    return map.size ? map : null;
  }

  BCV.quizPage = { parse, fetchPage, advance, notShown, results, parseResults };
})();
