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

  /** An attempt handed in through a tool (New Quizzes and the like). Its address is the tool's
   *  launch point, which answers "Invalid launch" when opened on its own: it has to be launched
   *  by Canvas, signed, the way Canvas's own submission page does it. */
  function toolAttempt(c, a, cur) {
    const href = `${c.url}/external_tools/retrieve?display=borderless&assignment_id=${a.id}&url=${encodeURIComponent(cur.url)}`;
    const label = `Open attempt ${cur.attempt || 1}`;
    return h('a', {
      class: 'bcv-fb__link bcv-fb__toollink', href, target: '_blank', rel: 'noopener', text: label,
      onclick: (e) => {
        if (!BCV.exttool) return;
        e.preventDefault();
        BCV.exttool.open({ title: `${a.name} · attempt ${cur.attempt || 1}`, url: href, newTab: href, from: e.currentTarget });
      },
    });
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
      cur.url ? (cur.submission_type === 'basic_lti_launch' ? toolAttempt(c, a, cur) : h('a', { class: 'bcv-fb__link', href: cur.url, target: '_blank', rel: 'noopener', text: cur.url })) : null,
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
      wrap.replaceChildren(U.errorBox('This assignment could not be loaded.'), U.el('bcv-fb__btns', [h('button', { type: 'button', class: 'bcv-qz__big', text: 'Back to the assignment', onclick: () => app.go(backHref) })]));
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
    wrap.replaceWith(build(ctx, c, a, sub).el);
    return screen;
  }

  /** The screen's content, built once for the page and (2.98.55) for the box the mark opens into on
   *  the assignment page: { el, redraw, setSub }. In the box (opts.box) the way back out is the box's
   *  own × and Escape, so the row at its end offers the rubric alone. */
  function build(ctx, c, a, sub, { box = false } = {}) {
    const { app } = ctx;
    const wrap = U.el(`bcv-fb${box ? ' bcv-fb--box' : ''}`);
    const backHref = `${c.url}/assignments/${a.id}`;
    const me = String(store.env?.().current_user_id ?? '');

    function backRow() {
      const btns = [
        a?.rubric?.length ? CS().rubricMorph(h('button', { type: 'button', class: 'bcv-qz__big bcv-rubbtn', text: 'See the rubric', onclick: () => CS().openRubric(a, sub) }), a, sub) : null,
        box ? null : h('button', { type: 'button', class: 'bcv-qz__big', text: 'Back to the assignment', onclick: () => app.go(backHref) }),
        box ? null : h('button', { type: 'button', class: 'bcv-qz__big bcv-qz__big--primary', text: `Back to ${c.shortName || c.name}`, onclick: () => app.go(c.url) }),
      ].filter(Boolean);
      return btns.length ? U.el('bcv-fb__btns', btns) : null;
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

      // (in the box the header already carries the score, so only the instructor's words head the list)
      const scoreCard = box
        ? (topThread.length ? U.el('bcv-fb__scorecard bcv-fb__scorecard--box', [U.text('bcv-fb__kicker', 'From your instructor', 'span'), ...topThread.map((cm) => commentRow(cm, me))]) : null)
        : U.el('bcv-fb__scorecard', [
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
        : [U.emptyCard((a.submission_types || []).includes('external_tool') && graded ? 'Graded through the assignment’s tool: nothing was handed in here.' : 'Nothing has been handed in for this assignment yet.')];

      wrap.replaceChildren(...[
        scoreCard ? U.enter(scoreCard, 0, 45) : null,
        held,
        ...cards,
        composer(ctx, c, a, () => sub, (fresh) => { sub = fresh; }, () => { if (ctx.alive()) draw(); }),
        backRow(),
      ].filter(Boolean));
    }

    draw();
    return { el: wrap, redraw: () => { if (ctx.alive()) draw(); }, setSub: (fresh) => { sub = fresh; } };
  }

  // ---- (2.99.10) the mark's card: what the chip beside an assignment's title grows into ----------------------
  // One compact look (the mockups): the score's ring — the rubric's own ring where there is a rubric, a press on it
  // opening the rubric — with the score, the work's name, its course and kind; when it was handed in and when
  // marked; the instructor's words as bubbles; the attempts as tiles, the one that counts lit; and a comment, in
  // one pill with its send button. The feedback screen (build, above) stays the page's own, for a link or a phone.
  const NS = 'http://www.w3.org/2000/svg';
  const RUB = ['#5e5ce6', '#0a84ff', '#30b0c7', '#30d158', '#ff9f0a', '#ff375f']; // (the rubric ring's own palette, rubric-ring.js)
  /** The colour a share of the points wears: green from 80%, orange from 60%, red under. */
  const tone = (pct) => (pct === null ? 'var(--bcv-ink3)' : pct >= 80 ? 'var(--bcv-green)' : pct >= 60 ? 'var(--bcv-orange)' : 'var(--bcv-red)');
  const stamp = (v) => { const d = U.parse ? U.parse(v) : (v ? new Date(v) : null); return d && !Number.isNaN(+d) ? `${U.fmtShort(d)}, ${U.fmtTime(d)}` : ''; };
  const num = (v) => (v === null || v === undefined || v === '' || !Number.isFinite(Number(v)) ? null : Number(v));

  /** Where the mark stands: posted (a number to show), held back (marked, not released), or neither. */
  function standing(a, s) {
    const graded = s.workflow_state === 'graded' && num(s.score) !== null;
    const posted = graded && s.posted_at !== null;
    const possible = num(a.points_possible) || 0;
    const pct = posted && possible > 0 ? Math.round((Number(s.score) / possible) * 100) : null;
    return { graded, posted, held: graded && !posted, possible, pct, score: posted ? Number(s.score) : null };
  }

  function arc(cx, cy, r, from, to) { // (fractions of a turn, from twelve o'clock, clockwise)
    const pt = (f) => [cx + r * Math.sin(f * 2 * Math.PI), cy - r * Math.cos(f * 2 * Math.PI)];
    const [x0, y0] = pt(from), [x1, y1] = pt(to);
    return `M${x0.toFixed(2)} ${y0.toFixed(2)}A${r} ${r} 0 ${to - from > 0.5 ? 1 : 0} 1 ${x1.toFixed(2)} ${y1.toFixed(2)}`;
  }
  function path(d, stroke, width, cls = '') {
    const p = document.createElementNS(NS, 'path');
    p.setAttribute('d', d);
    p.setAttribute('fill', 'none');
    p.setAttribute('stroke', stroke);
    p.setAttribute('stroke-width', String(width));
    p.setAttribute('stroke-linecap', 'round');
    if (cls) p.setAttribute('class', cls);
    return p;
  }

  /** The card's ring. With a rubric: one slice per criterion, as long as its share of the points, lit as far as
   *  it was marked, in the rubric ring's colours — and a press on it opens the rubric. Without: the score's
   *  share, in its tone. The share in the middle either way (— before a mark is posted). */
  function ring(a, s, { onRubric = null } = {}) {
    const st = standing(a, s);
    const S = 56, C = S / 2, R = 23.5, Wd = 5.5;
    const svg = document.createElementNS(NS, 'svg');
    svg.setAttribute('viewBox', `0 0 ${S} ${S}`);
    svg.setAttribute('width', String(S));
    svg.setAttribute('height', String(S));
    svg.setAttribute('aria-hidden', 'true');
    svg.append(path(arc(C, C, R, 0, 0.9999), 'var(--bcv-fill2, rgba(120,120,128,.2))', Wd, 'bcv-mring__track'));
    const crit = (a.rubric || []).filter((cr) => num(cr.points) > 0);
    const assess = s.rubric_assessment || {};
    if (crit.length && st.posted) {
      const total = crit.reduce((n, cr) => n + Number(cr.points), 0);
      const gap = crit.length > 1 ? 0.035 : 0;
      let at = 0;
      crit.forEach((cr, i) => {
        const share = Number(cr.points) / total;
        const from = at + gap / 2, to = at + share - gap / 2;
        const got = num(assess[cr.id]?.points);
        const lit = got === null ? 0 : Math.max(0, Math.min(1, got / Number(cr.points)));
        svg.append(path(arc(C, C, R, from, to), RUB[i % RUB.length], Wd, 'bcv-mring__dim'));
        if (lit > 0) svg.append(path(arc(C, C, R, from, from + (to - from) * lit), RUB[i % RUB.length], Wd));
        at += share;
      });
    } else if (st.pct !== null && st.pct > 0) {
      svg.append(path(arc(C, C, R, 0, Math.min(0.9999, st.pct / 100)), tone(st.pct), Wd, 'bcv-mring__fill'));
    }
    const mid = h('span', { class: 'bcv-mring__pct', style: { color: crit.length && st.posted ? 'var(--bcv-ink)' : tone(st.pct) }, text: st.pct === null ? '—' : `${st.pct}%` });
    const kids = [svg, mid];
    if (crit.length && onRubric) {
      return h('button', { type: 'button', class: 'bcv-mring bcv-mring--rubric', title: 'Open the rubric', 'aria-label': `Rubric${st.pct !== null ? `, ${st.pct}%` : ''}`, onclick: (e) => { e.stopPropagation(); onRubric(); } }, kids);
    }
    return h('span', { class: 'bcv-mring' }, kids);
  }

  /** The card's body: when, the words, the attempts, what was handed in, and a comment. */
  function card(ctx, c, a, sub) {
    const wrap = U.el('bcv-mcard');
    const me = String(store.env?.().current_user_id ?? '');
    const isTool = (a.submission_types || []).includes('external_tool');
    let pick = null; // (the attempt a tile was pressed for: its work and its words shown, a comment pinned to it; the latest by default)
    let body = null;

    function timeline(s, st) {
      const sent = s.submitted_at ? stamp(s.submitted_at) : null;
      const left = U.el('bcv-mcard__when', [
        U.text('bcv-mcard__k', s.late ? 'Submitted late' : 'Submitted', 'span'),
        U.text('bcv-mcard__v', sent || (isTool && st.graded ? 'Through the tool' : s.excused ? 'Excused' : 'Not yet'), 'span'),
      ]);
      const right = U.el('bcv-mcard__when bcv-mcard__when--end', [
        U.text('bcv-mcard__k', 'Graded', 'span'),
        U.text('bcv-mcard__v', st.posted ? stamp(s.graded_at || s.submitted_at) : st.held ? 'Not released' : s.excused ? 'Excused' : 'Waiting', 'span'),
      ]);
      const link = h('span', { class: `bcv-mcard__link ${st.posted ? 'is-done' : ''}`, style: { '--bcv-mtone': tone(st.pct) }, 'aria-hidden': 'true' });
      return U.el('bcv-mcard__row bcv-mcard__times', [left, link, right]);
    }

    function handedIn(cur) {
      if (!cur) return null;
      const bits = [];
      for (const f of cur.attachments || []) {
        bits.push(h('button', { type: 'button', class: 'bcv-mcard__chip', title: `Preview ${f.display_name || f.filename || 'the file'}`, onclick: () => BCV.viewer?.open(f) }, [
          U.text('bcv-mcard__chipk', fileKind(f), 'span'), U.text('bcv-mcard__chipn bcv-ellip', f.display_name || f.filename || 'File', 'span'),
        ]));
      }
      if (cur.url && cur.submission_type === 'basic_lti_launch') { const t = toolAttempt(c, a, cur); t.className = 'bcv-mcard__chip bcv-mcard__chip--link'; bits.push(t); }
      else if (cur.url) bits.push(h('a', { class: 'bcv-mcard__chip bcv-mcard__chip--link bcv-ellip', href: cur.url, target: '_blank', rel: 'noopener', text: cur.url.replace(/^https?:\/\//, '') }));
      if (cur.body) bits.push(U.text('bcv-mcard__quote bcv-pretty', String(BCV.utils.htmlToText ? BCV.utils.htmlToText(cur.body, 160) : cur.body)));
      return bits.length ? U.el('bcv-mcard__row bcv-mcard__handed', bits) : null;
    }

    function bubble(cm) {
      const mine = me && String(cm.author_id ?? '') === me;
      const who = mine ? 'You' : (cm.author_name || cm.author?.display_name || 'Your instructor');
      const media = cm.media_comment || null;
      return U.el(`bcv-mcard__bubble ${mine ? 'is-mine' : ''}`, [
        U.el('bcv-mcard__btext', [
          cm.comment ? h('p', { class: 'bcv-pretty', text: cm.comment }) : null,
          media?.url ? h('a', { href: media.url, target: '_blank', rel: 'noopener', text: `${media.media_type === 'video' ? 'Video' : 'Voice'} comment` }) : null,
          ...(cm.attachments || []).map((f) => h('button', { type: 'button', class: 'bcv-mcard__blink', text: f.display_name || f.filename || 'Attachment', onclick: () => BCV.viewer?.open(f) })),
        ]),
        U.text('bcv-mcard__bwho', [who, cm.created_at ? stamp(cm.created_at) : null].filter(Boolean).join(' · ')),
      ]);
    }

    function attempts(all, s, st, cur) {
      const scored = all.map((x) => num(x.score));
      if (all.length < 2) return null;
      const counted = num(s.score);
      // the attempt that counts: the latest whose score is the one Canvas holds (a quiz kept at its highest, say)
      let kept = -1;
      for (let i = all.length - 1; i >= 0; i--) if (counted !== null && scored[i] === counted) { kept = i; break; }
      // what Canvas kept, in a word: the best of several scored attempts is the highest kept (a quiz's own way, even
      // when it is the latest too); the only one scored, being the last, is the latest kept
      const marks = scored.filter((v) => v !== null);
      const label = kept < 0 || !st.posted ? null : marks.length > 1 && scored[kept] === Math.max(...marks) ? 'Highest kept' : kept === all.length - 1 ? 'Latest kept' : null;
      return U.el('bcv-mcard__row bcv-mcard__attempts', [
        U.el('bcv-mcard__head', [U.text('bcv-mcard__k', 'Attempts', 'span'), label ? U.text('bcv-mcard__k', label, 'span') : null]),
        U.el('bcv-mcard__tiles', all.map((x, i) => h('button', {
          type: 'button', class: `bcv-mcard__tile ${i === kept && st.posted ? 'is-kept' : ''} ${x === cur ? 'is-on' : ''}`,
          'aria-pressed': String(x === cur), style: i === kept && st.posted ? { '--bcv-mtone': tone(st.pct) } : null,
          onclick: (e) => { e.stopPropagation(); pick = Number(x.attempt) || i + 1; draw(); },
        }, [
          U.text('bcv-mcard__tilek', `Attempt ${x.attempt || i + 1}`, 'span'),
          U.text('bcv-mcard__tilev', scored[i] !== null && (i !== all.length - 1 || st.posted) ? `${store.fmtPts(scored[i])} / ${store.fmtPts(st.possible)}` : '—', 'span'),
        ]))),
      ]);
    }

    /** The attempt on show: the one a tile was pressed for, else the latest. */
    function current(s) {
      const all = attemptsOf(s);
      return all.find((x) => pick !== null && Number(x.attempt) === pick) || all[all.length - 1] || null;
    }

    function compose() {
      const input = h('input', { class: 'bcv-mcard__input', type: 'text', placeholder: 'Comment to instructor', 'aria-label': 'Comment to your instructor' });
      let busy = false;
      const send = h('button', { type: 'button', class: 'bcv-mcard__send', 'aria-label': 'Send', disabled: true }, U.svg('M12 19V5M5 12l7-7 7 7', { size: 15, stroke: 'currentColor', width: 2.4 }));
      input.addEventListener('input', () => { send.disabled = busy || !input.value.trim(); });
      const go = async () => {
        const text = input.value.trim();
        if (!text || busy) return;
        busy = true;
        send.disabled = true;
        input.value = '';
        try {
          await store.commentOnSubmission(c.id, a.id, text, current(sub)?.attempt || null);
          const fresh = await store.submission(c.id, a.id, { force: true }).catch(() => null);
          if (fresh) sub = fresh;
          if (ctx.alive()) draw();
        } catch (e) {
          input.value = text; // put it back rather than losing it
          U.toast(`The comment was not sent: ${e?.message || e}`, { error: true });
        } finally {
          busy = false;
          send.disabled = !input.value.trim();
        }
      };
      send.addEventListener('click', go);
      input.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); go(); } });
      return U.el('bcv-mcard__foot', [
        U.el('bcv-mcard__pill', [input, send]),
        U.text('bcv-mcard__perm', 'Sent comments can’t be edited or deleted.'),
      ]);
    }

    function draw() {
      const s = sub;
      const st = standing(a, s);
      const all = attemptsOf(s);
      const latest = all[all.length - 1] || null;
      const cur = current(s);
      const words = commentsFor(s, cur?.attempt, cur === latest);
      const next = U.el('bcv-mcard__body', [
        timeline(s, st),
        handedIn(cur),
        words.length ? U.el('bcv-mcard__row bcv-mcard__words', words.map(bubble)) : null,
        attempts(all, s, st, cur),
      ].filter(Boolean));
      if (body) body.replaceWith(next); else wrap.prepend(next);
      body = next;
    }

    wrap.append(compose()); // (built once, so a comment half written outlives a redraw)
    draw();
    return { el: wrap, redraw: () => { if (ctx.alive()) draw(); }, setSub: (fresh) => { sub = fresh; } };
  }

  /** The kind of work in the student's words (the card's second line). */
  function kindOf(a, s) {
    const all = attemptsOf(s);
    const t = all[all.length - 1]?.submission_type;
    if (t && TYPE_WORD[t]) return TYPE_WORD[t];
    const first = (a.submission_types || [])[0];
    return { online_quiz: 'Online quiz', external_tool: 'External tool', online_upload: 'File upload', online_text_entry: 'Text entry', online_url: 'Website URL', discussion_topic: 'Discussion', media_recording: 'Media recording', student_annotation: 'Annotation', on_paper: 'On paper', none: 'No submission' }[first] || 'Assignment';
  }

  BCV.screens.feedback = { render, build, card, ring, kindOf, standing };
})();
