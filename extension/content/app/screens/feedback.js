/* Assignment feedback: what a mark actually says, on a screen of its own.
 *
 * The same shape as a quiz's feedback (mockup 9) — a score card, then one card per unit of work,
 * then the way back — except that an assignment's unit is an attempt rather than a question. It
 * replaced a sheet over the assignment page: a sheet has to hold everything at once, and the file
 * preview it framed is Canvas's own document service, which answers "service unavailable" often
 * enough that the sheet read as broken. Here nothing is framed — a file is opened or downloaded,
 * and a preview that will not come is one button failing, not the screen.
 */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;
  const CS = () => BCV.screens.course;

  const CHECK = 'M20 6L9 17l-5-5';
  const CLOCK = 'M12 5a8 8 0 100 16 8 8 0 000-16zM12 9v4l3 2';

  /** Canvas's words for a submission type, in the student's. */
  const TYPE_WORD = {
    online_upload: 'File upload', online_text_entry: 'Text entry', online_url: 'Website URL',
    online_quiz: 'Online quiz', discussion_topic: 'Discussion', media_recording: 'Media recording',
    student_annotation: 'Annotation', basic_lti_launch: 'External tool', on_paper: 'On paper', none: 'Nothing to submit',
  };
  const fileSize = (n) => {
    const v = Number(n);
    if (!Number.isFinite(v) || v <= 0) return null;
    return v < 1024 ? `${v} B` : v < 1024 * 1024 ? `${Math.round(v / 1024)} KB` : `${(v / (1024 * 1024)).toFixed(1)} MB`;
  };
  const fileKind = (f) => {
    const t = String(f['content-type'] || f.mime_class || '').toLowerCase();
    const name = String(f.display_name || f.filename || '');
    if (/pdf/.test(t)) return 'PDF';
    if (/^image\//.test(t) || t === 'image') return 'IMG';
    if (/word|document|^text\//.test(t) || t === 'doc') return 'DOC';
    if (/sheet|excel|csv/.test(t)) return 'XLS';
    if (/zip|compressed/.test(t)) return 'ZIP';
    const ext = name.includes('.') ? name.split('.').pop().toUpperCase() : '';
    return ext.slice(0, 4) || 'FILE';
  };

  /** Every attempt the student made, oldest first. Canvas keeps them in submission_history; an
   *  assignment handed in once has none, and is its own single attempt. */
  function attemptsOf(s) {
    const hist = (s.submission_history || []).filter((x) => x && (x.submitted_at || x.attempt));
    const list = hist.length ? hist : (s.submitted_at || s.attempt ? [s] : []);
    return list.slice().sort((x, y) => (Number(x.attempt) || 0) - (Number(y.attempt) || 0));
  }

  /** The comments filed against one attempt. Canvas pins each comment to the attempt it was written
   *  on; unfiltered, feedback on a first draft comes back as feedback on the final hand-in. A
   *  comment with no attempt at all belongs to the latest. */
  // (a comment that is a voice or video note, or files alone, is a comment too)
  const commentsFor = (s, attempt, isLatest) => (s.submission_comments || [])
    .filter((cm) => cm && (cm.comment || cm.media_comment || (cm.attachments || []).length))
    .filter((cm) => (cm.attempt ? Number(cm.attempt) === Number(attempt || 1) : isLatest));

  function commentRow(cm, me) {
    const who = cm.author_name || cm.author?.display_name || (String(cm.author_id ?? '') === me ? 'You' : 'Comment');
    const mine = me && String(cm.author_id ?? '') === me;
    const media = cm.media_comment || null;
    const files = cm.attachments || [];
    return U.el(`bcv-fb__comment ${mine ? 'is-mine' : ''}`, [
      h('span', { class: 'bcv-fb__avatar', text: U.initials(who) || '·' }),
      U.el('bcv-fb__cbody', [
        U.text('bcv-fb__ctitle', `${mine ? 'You' : who}${cm.created_at ? ` · ${U.fmtAt(cm.created_at)}` : ''}`),
        cm.comment ? h('p', { class: 'bcv-fb__ctext bcv-pretty', text: cm.comment }) : null,
        // a voice or video note: played where it is, or opened in a tab when the browser cannot
        media ? (media.url ? h(media.media_type === 'video' ? 'video' : 'audio', { class: 'bcv-fb__media', controls: '', preload: 'none', src: media.url }) : null) : null,
        media ? h('a', { class: 'bcv-fb__medialink', href: media.url || '#', target: '_blank', rel: 'noopener', text: `${media.media_type === 'video' ? 'Video' : 'Voice'} comment${media.display_name ? ` · ${media.display_name}` : ''}` }) : null,
        files.length ? U.el('bcv-fb__files', files.map(fileRow)) : null,
      ]),
    ]);
  }

  /** One handed-in file. Two ways at it on purpose: the preview is Canvas's document service and can
   *  be down, and a download that always works is what makes that survivable. The file is passed as
   *  Canvas just handed it back — it is the student's own, not the course's, so it carries no course
   *  context — and nothing about it is kept, because these links expire. */
  function fileRow(f) {
    const size = fileSize(f.size);
    return U.el('bcv-fb__file', [
      U.text('bcv-fb__filekind', fileKind(f), 'span'),
      U.el('bcv-fb__filebody', [
        U.text('bcv-fb__filename bcv-ellip', f.display_name || f.filename || 'File', 'span'),
        size ? U.text('bcv-fb__filesize', size, 'span') : null,
      ]),
      h('button', { type: 'button', class: 'bcv-fb__fileact', text: 'Preview', onclick: () => BCV.viewer?.open(f) }),
      f.url ? h('a', { class: 'bcv-fb__fileact bcv-fb__fileact--dl', href: f.url, download: f.filename || f.display_name || '', text: 'Download' }) : null,
    ]);
  }

  /** One attempt, as a question card is one question: what was handed in, when, and what came back. */
  function attemptCard(ctx, c, a, s, cur, { isLatest, graded, me }, i) {
    const dark = ctx.app.isDark();
    const score = cur.score === null || cur.score === undefined ? null : Number(cur.score);
    const possible = a.points_possible;
    const marked = score !== null && (isLatest ? graded : true); // (an earlier attempt keeps the score it got; the latest counts only once posted)
    const [ink, tint] = marked ? (dark ? ['#5ddb7d', 'rgba(52,199,89,.2)'] : ['#1e7a37', 'rgba(52,199,89,.14)'])
      : ['var(--bcv-ink3)', 'var(--bcv-fill)'];
    const files = cur.attachments || [];
    const thread = commentsFor(s, cur.attempt, isLatest);
    const type = TYPE_WORD[cur.submission_type] || cur.submission_type || null;
    return U.enter(U.el('bcv-fb__q', [
      U.el('bcv-fb__qhead', [
        h('span', { class: 'bcv-fb__mark', style: { background: tint } }, U.svg(marked ? CHECK : CLOCK, { size: 13, stroke: ink, width: 2.6 })),
        h('span', { class: 'bcv-fb__qn', text: `Attempt ${cur.attempt || 1}` }),
        isLatest ? U.text('bcv-fb__tag', 'Latest', 'span') : null,
        h('span', { class: 'bcv-fb__score', style: { color: ink }, text: marked ? `${store.fmtPts(score)} / ${possible ?? '—'}` : 'Not graded' }),
      ]),
      U.el('bcv-fb__facts', [
        ['Submitted', cur.submitted_at ? U.fmtAt(cur.submitted_at) : 'Not submitted'],
        ['Type', type],
        ['Graded', marked && isLatest && s.graded_at ? U.fmtAt(s.graded_at) : null],
        ['Late', cur.late ? (isLatest && s.points_deducted ? `${s.seconds_late ? `${Math.max(1, Math.round(s.seconds_late / 86400))} days · ` : ''}−${store.fmtPts(s.points_deducted)} pts` : 'Yes') : null], // (Canvas's late penalty, where it took one)
      ].filter(([, v]) => v).map(([k, v]) => U.el('bcv-fb__fact', [U.text('bcv-fb__factk', k, 'span'), U.text('bcv-fb__factv', v, 'span')]))),
      files.length ? U.el('bcv-fb__files', files.map(fileRow)) : null,
      // a text entry or a URL is the hand-in itself: it is shown, not described
      cur.body ? CS().prose(cur.body, { cls: 'bcv-fb__body' }) : null,
      cur.url ? h('a', { class: 'bcv-fb__link', href: cur.url, target: '_blank', rel: 'noopener', text: cur.url }) : null,
      thread.length ? U.el('bcv-fb__thread', [U.text('bcv-fb__kicker', 'Comments on this attempt', 'span'), ...thread.map((cm) => commentRow(cm, me))]) : null,
      !isLatest ? U.text('bcv-fb__note bcv-pretty', 'Only your latest attempt is graded, and a comment can only be added on it.') : null,
    ]), i, 45);
  }

  /** The composer, kept from the sheet this screen replaced: a comment to the instructor, pinned to
   *  the attempt it was written on, with the text put back rather than lost when it will not send. */
  function composer(ctx, c, a, getSub, setSub, redraw) {
    const s = getSub();
    const all = attemptsOf(s);
    const latest = all[all.length - 1] || null;
    const input = h('input', { class: 'bcv-fb__input', type: 'text', placeholder: 'Add a comment', 'aria-label': 'Add a comment' });
    let busy = false;
    const send = h('button', {
      type: 'button', class: 'bcv-fb__send', text: 'Send',
      onclick: async () => {
        const text = input.value.trim();
        if (!text || busy) return;
        busy = true;
        send.disabled = true;
        input.value = '';
        try {
          await store.commentOnSubmission(c.id, a.id, text, latest?.attempt || null);
          const fresh = await store.submission(c.id, a.id, { force: true }).catch(() => null);
          if (fresh) setSub(fresh);
          redraw();
        } catch (e) {
          busy = false;
          send.disabled = false;
          input.value = text; // put it back rather than losing it
          U.toast(`The comment was not sent: ${e?.message || e}`, { error: true });
        }
      },
    });
    return U.el('bcv-fb__card bcv-fb__composer', [
      U.text('bcv-fb__kicker', 'Reply to your instructor', 'span'),
      U.el('bcv-fb__compose', [input, send]),
      U.text('bcv-fb__perm', 'Comments go to your instructor and cannot be edited or deleted once sent.'),
    ]);
  }

  async function render(ctx, shell) {
    const { app, route } = ctx;
    const c = shell.course;
    // the quiz feedback's own chrome: the course column, under the course header, nothing taken over
    const screen = U.el('bcv-qz is-embedded is-feedback');
    const body = h('div', { class: 'bcv-qz__body' });
    const wrap = U.el('bcv-fb');
    body.append(wrap);
    screen.append(body);
    wrap.append(U.loading('rows', 3));
    const aid = route.arg;
    const backHref = `${c.url}/assignments/${aid}`;

    let [a, sub] = await Promise.all([
      store.assignment(c.id, aid).catch(() => null),
      store.submission(c.id, aid, { force: true }).catch(() => null),
    ]);
    if (!ctx.alive()) return screen;
    if (!a) {
      wrap.replaceChildren(U.errorBox('This assignment could not be loaded.'), backRow());
      return screen;
    }
    // Canvas answers this one from a different service to the rest, and it is the one that goes down:
    // say so and offer the way back to the page, rather than drawing a screen with nothing on it.
    if (!sub) {
      wrap.replaceChildren(
        U.errorBox('Canvas did not answer with your submission for this assignment. It is usually back within a minute.'),
        U.el('bcv-fb__btns', [
          h('button', { type: 'button', class: 'bcv-qz__big bcv-qz__big--primary', text: 'Try again', onclick: () => app.go(`${backHref}?bcv=feedback&t=${Date.now()}`) }),
          h('button', { type: 'button', class: 'bcv-qz__big', text: 'Back to the assignment', onclick: () => app.go(backHref) }),
        ]),
      );
      return screen;
    }
    app.nameHere?.(a.name); // the next screen's Back names this assignment
    const me = String(store.env?.().current_user_id ?? '');

    function backRow() {
      return U.el('bcv-fb__btns', [
        a?.rubric?.length ? h('button', { type: 'button', class: 'bcv-qz__big bcv-rubbtn', text: 'See the rubric', onclick: () => CS().openRubric(a, sub) }) : null,
        h('button', { type: 'button', class: 'bcv-qz__big', text: 'Back to the assignment', onclick: () => app.go(backHref) }),
        h('button', { type: 'button', class: 'bcv-qz__big bcv-qz__big--primary', text: `Back to ${c.shortName || c.name}`, onclick: () => app.go(c.url) }),
      ].filter(Boolean));
    }

    function draw() {
      const s = sub;
      const all = attemptsOf(s);
      const latest = all[all.length - 1] || null;
      const graded = s.workflow_state === 'graded' && s.score !== null && s.score !== undefined;
      // Canvas posts a grade separately from marking it: posted_at === null means the instructor is
      // holding it back, and a number shown then is a number the student is not supposed to have.
      const posted = graded && s.posted_at !== null;
      const score = posted ? Number(s.score) : null;
      const possible = Number(a.points_possible) || 0;
      const pct = posted && possible > 0 ? Math.round((score / possible) * 100) : null;
      const when = posted ? `graded ${U.fmtAtUpper(s.graded_at || s.submitted_at)}`
        : graded ? 'marked, not yet released'
          : s.submitted_at ? 'awaiting your instructor’s mark' : 'nothing handed in yet';
      // the instructor's own words on the graded attempt head the screen; each attempt keeps its own
      const topThread = commentsFor(s, latest?.attempt, true).filter((cm) => !me || String(cm.author_id ?? '') !== me);

      const scoreCard = U.el('bcv-fb__scorecard', [
        U.el('bcv-fb__scoreline', [
          h('span', { class: 'bcv-fb__big', text: posted ? `${store.fmtPts(score)} / ${store.fmtPts(possible)}` : '—' }),
          pct !== null ? h('span', { class: 'bcv-fb__pct', text: `${pct}%` }) : null,
          h('span', { class: 'bcv-fb__summary', text: `${a.name} · ${when}` }),
        ]),
        U.el('bcv-fb__bar', h('div', { class: 'bcv-fb__fill', style: { width: `${pct ?? 0}%` } })),
        ...topThread.map((cm) => commentRow(cm, me)),
      ]);
      const held = graded && !posted
        ? U.hint('Your instructor has marked this but has not released the grade yet, so no number is shown — an unreleased score reads exactly like a real one.', 'bcv-hint--narrow')
        : null;
      const cards = all.length
        ? all.slice().reverse().map((cur, i) => attemptCard(ctx, c, a, s, cur, { isLatest: cur === latest, graded: posted, me }, i + 1))
        : [U.emptyCard('Nothing has been handed in for this assignment yet.')];

      wrap.replaceChildren(...[
        U.enter(scoreCard, 0, 45),
        held,
        ...cards,
        composer(ctx, c, a, () => sub, (fresh) => { sub = fresh; }, () => { if (ctx.alive()) draw(); }),
        backRow(),
      ].filter(Boolean));
    }

    draw();
    return screen;
  }

  BCV.screens.feedback = { render };
})();
