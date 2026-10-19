/* Item views inside a course: assignment, discussion thread / announcement,
 * page, quiz and syllabus. The mockup has no screens for these, so they
 * are interpreted in the same language: one reading card, one side column. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h, htmlToText, escapeHtml } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  /** How a quiz is restricted, in words: an access code, an IP filter, Respondus LockDown Browser. */
  const hasGrade = (a) => { const s = a?.submission || {}; return s.workflow_state === 'graded' && s.score !== null && s.score !== undefined && s.posted_at !== null; }; // (marked and posted: a held mark is not yours to see yet)
  const store = BCV.store;
  const CS = () => BCV.screens.course;

  const cols = () => U.el('bcv-body bcv-body--course-cols');
  const mainCol = () => h('div', { class: 'bcv-col', style: { flex: '1 1 560px' } });
  const sideCol = () => h('div', { class: 'bcv-col bcv-col--16', style: { flex: '1 1 300px' } });
  const meta = (pairs) => U.el('bcv-detail__meta', pairs.filter(([, v]) => v !== null && v !== undefined && v !== '').map(([k, v]) => h('span', { class: 'bcv-detail__meta-item' }, [h('b', { text: `${k} ` }), String(v)])));
  // (2.99.20) the name on one line, cut short with an ellipsis (the whole of it on hover): a long item's name wrapped onto two
  const backBtn = (app, href, lbl) => h('button', { type: 'button', class: 'bcv-linkbtn bcv-detail__back', title: lbl, onclick: () => { app.markBack?.(); app.go(href); } }, [U.svg(IC.back, { size: 14, stroke: 'var(--bcv-blue)', width: 2.1 }), h('span', { class: 'bcv-detail__backlabel bcv-ellip', text: lbl })]);
  /** The item's Back: the screen it was opened from (Modules, the Dashboard, another item…), else the list it belongs to. */
  const backTo = (app, href, lbl) => { const b = app.backTo ? app.backTo({ href, label: lbl }) : { href, label: lbl }; return backBtn(app, b.href, b.label); };
  const nativeHref = (path) => `${path}${path.includes('?') ? '&' : '?'}bcv=native`;
  /** "10 pts" from a points field, or nothing when Canvas carries no points for the item (never "null pts"). */
  const pts = (v) => (v === null || v === undefined || v === '' ? null : `${store.fmtPts(v)} pts`);
  /** Canvas says −1 for unlimited attempts, and nothing at all where the assignment does not limit them. */
  const attemptsFact = (a, s) => (a.allowed_attempts > 0 ? `${s.attempt || 0} of ${a.allowed_attempts}`
    : a.allowed_attempts === -1 ? `${s.attempt || 0} of unlimited` : null);

  /** The mark's destination. (2.98.55) Pressed on the page, the chip opens where it is: it grows into
   *  a box the way a Dashboard counter does — the page dims and blurs round it from the press, its
   *  words glide into the box's header — and the box holds the feedback screen's content (every
   *  attempt, what was handed in, the thread, a reply), the page staying put under it; Escape, the ×
   *  or a press outside fold it back into the chip. With no chip to grow from (a link, the phone) the
   *  feedback screen is still a screen of its own — the same shape as a quiz's — where a file is
   *  opened or downloaded rather than framed (Canvas's own document preview answers "service
   *  unavailable" often enough that a sheet built around it read as broken). */
  const openMark = (ctx, c, a, x = null, from = null, opts = {}) => {
    const page = () => ctx.app.go(`${c.url}/assignments/${a.id}?bcv=feedback`);
    if (x && from?.isConnected && !BCV.phone?.active?.()) {
      const ringReady = !a.rubric?.length || !!BCV.rubricRing; // (a rubric's card wears the rubric's own ring: its code first)
      if (BCV.screens.feedback?.build && ringReady) { openMarkBox(ctx, c, a, x, from, opts); return; }
      // (2.99.10) the box's insides are the feedback screen, which is code on demand (lazy.js, with the hand-in
      // block): an assignment with no hand-in block — a tool's, a quiz's — had not loaded it, and the box fell
      // through to the page. It is loaded here first, then the box opens; the page only if it cannot be had.
      // (A rubric ring that cannot be had is no reason not to open: the grade ring stands in for it.)
      if (BCV.lazy?.load) {
        Promise.all([BCV.lazy.load('submit'), ringReady ? null : BCV.lazy.load('rubric').catch(() => null)]).then(() => {
          if (!from.isConnected) return;
          if (BCV.screens.feedback?.build) openMarkBox(ctx, c, a, x, from, opts); else if (!opts.hover) page();
        }).catch(() => { if (!opts.hover) page(); });
        return;
      }
    }
    page();
  };
  /** (2.99.13) A box that grows out of what opened it (U.cardBox): the mark's card from a grade, the hand-in panel from the
   *  assignment's big pill. `make(api)` fills it — { cls, kids, measure(), glide?: [[box's word, button's word]] } — and
   *  can call api.swap(make2) to turn it into something else in place (the card's Submit again), api.relayout() when what
   *  it holds changes height, api.close() to fold it back. `anchor` is where it grows from: 'right' (hung from a chip's
   *  corner at the end of a row), 'left' (from a button's start), or 'center' (out of the button's middle, every way). */
  function growBox(ctx, from, { label = '', W = 400, H = 560, anchor = 'right', hover = false, onClosed = null, vars = null, within = null } = {}, make) {
    const prev = document.querySelector('.bcv-sheet-ov');
    if (prev?.bcvReopen && prev.bcvCard === from && prev.classList.contains('is-folding')) { prev.bcvReopen(); return prev.bcvApi; } // (pressed again as it folds: it opens again from where it is)
    if (prev) { clearTimeout(prev._bcvFoldT); prev.remove(); }
    const ov = U.el('bcv-sheet-ov bcv-sheet-ov--card is-far', null, { role: 'dialog', 'aria-label': label }); // (is-far: the dim and blur start out wide, to close in on the box)
    let folding = false;
    let box = null; // (U.cardBox: the box laid out once where it goes, drawn through a clip that grows out of the button)
    let width = W, boxH = H; // (as tall as its content wants, up to H: measured once built)
    const M = 16;
    function geometry(back) {
      const r = from.getBoundingClientRect();
      if (back) return { x: r.left, y: r.top, w: r.width, h: r.height };
      const vw = innerWidth, vh = innerHeight, w = Math.min(width, vw - 2 * M), hgt = Math.min(boxH, vh - 2 * M);
      const x = anchor === 'center' ? r.left + r.width / 2 - w / 2 : anchor === 'left' ? r.left : r.right - w;
      const y = anchor === 'center' ? r.top + r.height / 2 - hgt / 2 : r.top; // (centre: the box's middle on the button's, growing out of it every way)
      // (2.99.15) `within`: kept inside the page's own card (over the assignment, not the course's rail beside it)
      const b = within?.isConnected ? within.getBoundingClientRect() : null;
      const lo = b ? Math.max(M, b.left + 16) : M, hi = b ? Math.min(vw - M, b.right - 16) : vw - M;
      return { x: Math.max(lo, Math.min(x, hi - w)), y: Math.max(M, Math.min(y, vh - M - hgt)), w, h: hgt };
    }
    // Escape from anywhere on the page folds the box (a reply just sent leaves the cursor nowhere in particular); a file's viewer or a question over the box takes its own Escape first
    const onKey = (e) => {
      if (e.key !== 'Escape' || e.defaultPrevented) return;
      if (!ov.isConnected) { document.removeEventListener('keydown', onKey); return; }
      if (document.querySelector('.bcv-viewer-ov, .bcv-sheet-ov:not(.bcv-sheet-ov--card)')) return;
      e.stopPropagation();
      close();
    };
    let lastPt = null; // (where the pointer was last seen over the box's layer)
    const close = () => {
      if (folding || !ov.isConnected) return;
      folding = true;
      document.removeEventListener('keydown', onKey);
      U.hoverCool(from, lastPt); // (a pointer still over the button as the box folds into it does not open it again until it has left)
      ov.classList.add('is-folding', 'is-far'); // (the focus lets go outward as the box folds)
      box.fold(); // (from wherever it is drawn — mid-growth too — back into the button, its word back to the button's)
      from.setAttribute?.('aria-expanded', 'false');
      clearTimeout(ov._bcvFoldT);
      ov._bcvFoldT = setTimeout(() => { ov.remove(); from.classList.remove('bcv-grow-src'); onClosed?.(); }, U.reducedMotion() ? 0 : 560);
      // (2.99.15) the layer fades away once the box has all but landed (.bcv-sheet-ov--card: opacity, .3s in): the real
      // button is back under it from that moment, so the box dissolves onto the button rather than the button fading
      // out with it and popping back after
      clearTimeout(ov._bcvSrcT);
      ov._bcvSrcT = setTimeout(() => from.classList.remove('bcv-grow-src'), U.reducedMotion() ? 0 : 300);
      if (from.isConnected) from.focus?.({ preventScroll: true }); // (keyboard users land back on what opened it)
    };
    // pressed again as it folds: the fold is called off and it opens again from where it has got to
    ov.bcvCard = from;
    ov.bcvReopen = () => {
      clearTimeout(ov._bcvFoldT);
      clearTimeout(ov._bcvSrcT);
      folding = false;
      ov.classList.remove('is-folding', 'is-far');
      document.addEventListener('keydown', onKey);
      box.reopen();
      from.classList.add('bcv-grow-src');
      from.setAttribute?.('aria-expanded', 'true');
      ov.focus();
    };
    ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
    document.addEventListener('keydown', onKey);
    ov.addEventListener('pointermove', (e) => { lastPt = { x: e.clientX, y: e.clientY }; }, { passive: true });
    const sheet = U.el('bcv-sheet bcv-sheet--card bcv-grow is-at-card'); // (bcv-grow: the button's own look while it is the button's size)
    for (const [k, v] of Object.entries(vars || {})) if (v) sheet.style.setProperty(k, v); // (the page's own colours: the box lives outside it)
    let content = null;
    const measure = () => Math.min(H, Math.max(120, Math.ceil(content.measure())));
    const api = {
      ov, sheet, close, folding: () => folding,
      /** What it holds changed height: the box takes its new size, the clip growing (or shrinking) to it. */
      relayout() { if (!ov.isConnected || folding || !box) return; boxH = measure(); box.relayout(); },
      /** Another width (the mark's card unfolding its comments): laid out at it, the clip growing to it from where it is. */
      widen(w2) {
        if (!ov.isConnected || folding || !box) return;
        width = w2;
        // (measured as it will stand — at the new width, for a moment, before anything is painted — then the clip grows
        // to it from where the box is drawn now)
        const was = sheet.style.width;
        sheet.style.width = `${Math.min(w2, innerWidth - 32)}px`;
        boxH = measure();
        sheet.style.width = was;
        box.relayout();
      },
      /** Turn into something else in place (the card's Submit again → the hand-in panel). */
      swap(make2, { W: w2 = width } = {}) {
        if (folding) return;
        for (const k of [...sheet.children]) if (!k.classList.contains('bcv-mark__ghost')) k.remove();
        if (content?.cls) sheet.classList.remove(...content.cls.split(/\s+/).filter(Boolean));
        content = make2(api);
        if (content.cls) sheet.classList.add(...content.cls.split(/\s+/).filter(Boolean));
        sheet.append(...content.kids.filter(Boolean));
        width = w2;
        box.layout();
        boxH = measure();
        box.relayout();
        content.after?.();
      },
    };
    ov.bcvApi = api;
    content = make(api);
    if (content.cls) sheet.classList.add(...content.cls.split(/\s+/).filter(Boolean));
    sheet.append(...content.kids.filter(Boolean));
    ov.append(sheet, U.el('bcv-card-blur', null, { 'aria-hidden': 'true' })); // (the page blurred round the box: a layer of its own)
    // the box wears the button's own look while it is the button's size — its fill, edge and corners — and a copy of the
    // button's words sits over its header, so the first frame is the button exactly, and the last frame of the fold too;
    // the copy fades as the box grows (the box's own words fade in), all but the one word that glides, left out of the copy
    // (a fill that is a tint over the card it sits on is flattened onto that card here, or the box would be see-through
    // while it is the button, the page and the button itself showing through it)
    // (2.99.14) any CSS colour, read by painting it: a button's fill written as color-mix() comes back from
    // getComputedStyle as color(srgb …) or oklab(…), which no rgba() pattern reads — and the box lost the button's colour)
    const rgba = (str) => {
      if (!str || str === 'transparent') return null;
      const m = /^rgba?\(([^)]+)\)$/.exec(str);
      if (m) { const [r, g, b, a = 1] = m[1].split(/[\s,/]+/).filter(Boolean).map((x) => parseFloat(x)); return { r, g, b, a: Number.isFinite(a) ? a : 1 }; }
      try {
        const cv = (rgba.cv ||= Object.assign(document.createElement('canvas'), { width: 1, height: 1 }));
        const cx = cv.getContext('2d', { willReadFrequently: true });
        cx.clearRect(0, 0, 1, 1);
        cx.fillStyle = '#000';
        cx.fillStyle = str;
        cx.fillRect(0, 0, 1, 1);
        const [r, g, b, a] = cx.getImageData(0, 0, 1, 1).data;
        return { r, g, b, a: a / 255 };
      } catch { return null; }
    };
    const under = (el) => { for (let e = el.parentElement; e; e = e.parentElement) { const c = rgba(getComputedStyle(e).backgroundColor); if (c && c.a > 0) return c; } return { r: 255, g: 255, b: 255, a: 1 }; };
    const flat = (top, base) => `rgb(${Math.round(top.r * top.a + base.r * (1 - top.a))}, ${Math.round(top.g * top.a + base.g * (1 - top.a))}, ${Math.round(top.b * top.a + base.b * (1 - top.a))})`;
    const cs = getComputedStyle(from), r0 = from.getBoundingClientRect(), base = under(from);
    const bg = rgba(cs.backgroundColor), edge = rgba(cs.borderTopColor);
    const bgFlat = bg && bg.a > 0 ? flat(bg, base) : flat({ ...base, a: 1 }, base);
    sheet.style.setProperty('--bcv-mark-bg', bgFlat);
    sheet.style.setProperty('--bcv-mark-edge', edge && edge.a > 0 && parseFloat(cs.borderTopWidth) > 0 ? flat(edge, rgba(bgFlat)) : bgFlat);
    sheet.style.setProperty('--bcv-mark-r', cs.borderTopLeftRadius);
    const glide = (content.glide || []).filter(([el, b]) => el && b);
    const copy = h('div', { class: 'bcv-mark__ghost', 'aria-hidden': 'true', style: { color: cs.color, justifyContent: cs.justifyContent, alignItems: cs.alignItems, font: cs.font } });
    Object.assign(copy.style, { width: `${Math.max(0, r0.width - 2)}px`, height: `${Math.max(0, r0.height - 2)}px`, padding: cs.padding, columnGap: cs.columnGap }); // (inside the box's own 1px edge, laid out as the button lays its words)
    for (const n of from.childNodes) copy.append(n.cloneNode(true));
    for (const [, b] of glide) { const sel = b.dataset.glide; if (sel) copy.querySelector(`[data-glide="${sel}"]`)?.style.setProperty('visibility', 'hidden'); } // (that word is the box's, gliding)
    sheet.append(copy);
    box = U.cardBox({ ov, sheet, card: from, geometry: () => geometry(false), cardRadius: cs.borderTopLeftRadius });
    box.layout(); // (full size, unpainted: the content measured at the width it will have)
    document.body.append(ov);
    boxH = measure();
    // laid out where it goes, at its measured height, and drawn only where the button is — the copy of its words set over the
    // button's own place in it — before anything is painted: the first frame is the button
    const g0 = box.start();
    if (anchor !== 'right') Object.assign(copy.style, { left: `${Math.round(r0.left - g0.x)}px`, right: 'auto', top: `${Math.round(r0.top - g0.y)}px` });
    else Object.assign(copy.style, { right: `${Math.round(g0.x + g0.w - r0.right)}px`, top: `${Math.round(r0.top - g0.y)}px` });
    box.open(glide);
    // the button is what grows: the real one steps out while its box is up (the box's first and last frames are it
    // exactly), so nothing of it shows round the panel's edge — back as the box lands on it again
    from.classList.add('bcv-grow-src');
    from.setAttribute?.('aria-expanded', 'true');
    const onResize = () => { if (!ov.isConnected) { removeEventListener('resize', onResize); return; } if (!folding) box.relayout(); };
    addEventListener('resize', onResize);
    ov.tabIndex = -1;
    ov.focus();
    content.after?.();
    // opened by a hover, it folds again once the pointer has been off it a moment — unless it was
    // taken up (a press in it, a key, a field): then it stays, as a pressed one does
    if (hover) U.hoverHold(ov, sheet, close, () => folding);
    return api;
  }

  /** The mark's card (2.99.10, the mockups): the ring (the rubric's, where there is one: a press on it opens the rubric),
   *  the score, the work's name, its course and kind; then what feedback.card draws — when, the words, the attempts, a
   *  comment. `again` (2.99.13): a Submit again pill at its foot, turning the card into the hand-in panel in place. */
  function markContent(ctx, c, a, s, api, { value, label = '', when = '', again = null, glideFrom = null, side = false } = {}) {
    const scored = s.workflow_state === 'graded' && s.score !== null && s.score !== undefined;
    const posted = scored && s.posted_at !== null;
    const F = BCV.screens.feedback;
    const built = F.card(ctx, c, a, s, { side });
    // the ring pressed: the rubric takes the stage, the card stepping back under it, and comes back as the rubric goes
    // (onto the ring it was opened from). The rubric's own code was loaded before the card was built (openMark).
    const toRubric = () => {
      api.ov.classList.add('is-under');
      CS().openRubric(a, s);
      const t0 = Date.now();
      let seen = false;
      const back = () => {
        if (!api.ov.isConnected) return;
        const rr = document.querySelector('.bcv-rr-ov');
        seen = seen || !!rr;
        if (rr ? !rr.classList.contains('is-leaving') && !rr.classList.contains('is-closing') : !seen && Date.now() - t0 < 4000) { setTimeout(back, 90); return; }
        api.ov.classList.remove('is-under');
      };
      back();
    };
    const valueEl = U.text('bcv-sheet__value', value, 'span');
    const head = U.el('bcv-sheet__head bcv-mark__head', [
      h('span', { class: 'bcv-sheet__tile bcv-mark__ringslot' }, F.ring(a, s, { onRubric: toRubric })),
      U.el('bcv-sheet__titles', [
        U.el('bcv-sheet__line', [valueEl, label ? U.text('bcv-sheet__label', label, 'span') : null]),
        U.text('bcv-mark__title bcv-pretty', a.name),
        U.el('bcv-sheet__note bcv-mark__meta', [h('span', { class: 'bcv-mark__dot', style: { background: c.color || 'var(--bcv-ink3)' }, 'aria-hidden': 'true' }), U.text('bcv-ellip', [c.shortName || c.name, F.kindOf(a, s), when && !posted ? when : null].filter(Boolean).join(' · '), 'span')]),
      ]),
      h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: api.close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
    ]);
    const againRow = again ? U.el('bcv-mark__again', [h('button', { type: 'button', class: 'bcv-mark__againbtn', onclick: again }, [U.svg('M12 19V5M6 11l6-6 6 6', { size: 14, stroke: 'currentColor', width: 2.4 }), 'Submit again'])]) : null;
    // (under when it was handed in and marked: in sight as the card opens, not below the attempts)
    const placeAgain = () => { if (!againRow || againRow.isConnected) return; const t = built.el.querySelector('.bcv-mcard__times'); if (t) t.after(againRow); else built.el.querySelector('.bcv-mcard__body')?.prepend(againRow); };
    placeAgain();
    const keepAgain = placeAgain;
    // the page's copy of the submission is what the button was drawn from; Canvas is asked once more for what came since (a comment, another attempt), and the box redraws still if it answers with more
    store.submission(c.id, a.id, { force: true }).then((fresh) => {
      if (!fresh || !api.ov.isConnected || api.folding() || JSON.stringify(fresh) === JSON.stringify(s)) return;
      built.setSub(fresh);
      if (U.still) U.still(() => { built.redraw(); keepAgain(); }); else { built.redraw(); keepAgain(); }
    }).catch(() => {});
    if (side) {
      // (2.99.13) sideways: the mark and the work on the left, what is said about it on the right — its own column, the
      // newest words at its foot by the comment pill, the × at its top
      // (2.99.16) the comments start folded away: a small button in the card's own corner (beside the file, or a row of
      // its own) widens the card sideways to show them — the box growing to the right, the column fading in — and the
      // column's own button folds it back
      const n = (s.submission_comments || []).length;
      const BUBBLE = 'M5 6.5A2.5 2.5 0 017.5 4h9A2.5 2.5 0 0119 6.5v7a2.5 2.5 0 01-2.5 2.5H11l-4 3.5V16h0A2 2 0 015 14z';
      const cols = U.el('bcv-mark__cols is-folded');
      const unfold = (on) => {
        cols.classList.toggle('is-folded', !on);
        chatBtn.setAttribute('aria-expanded', String(on));
        right.inert = !on;
        api.widen(on ? 680 : 400);
        if (on) setTimeout(() => built.chat.querySelector('.bcv-mcard__opener')?.focus({ preventScroll: true }), 60); else chatBtn.focus({ preventScroll: true });
      };
      const chatBtn = h('button', { type: 'button', class: 'bcv-mark__chatbtn', 'aria-expanded': 'false', title: 'Show the comments', onclick: () => unfold(true) }, [U.svg(BUBBLE, { size: 14, stroke: 'currentColor', width: 2 }), h('span', { text: n ? `Comments · ${n}` : 'Comments' })]);
      const placeBtn = () => { // (into the row of what was handed in, where there is room beside it; else a row of its own)
        if (chatBtn.isConnected && chatBtn.parentElement !== built.el) return;
        const handed = built.el.querySelector('.bcv-mcard__handed');
        if (handed && !handed.querySelector('.bcv-mcard__quote')) handed.append(chatBtn);
        else built.el.querySelector('.bcv-mcard__body')?.append(U.el('bcv-mcard__row bcv-mark__chatrow', [chatBtn]));
      };
      placeBtn();
      new MutationObserver(placeBtn).observe(built.el, { childList: true }); // (the card redrawn — a comment sent, a tile pressed — keeps it)
      const left = U.el('bcv-mark__left', [head, U.el('bcv-mark__body', [built.el])]);
      const right = U.el('bcv-mark__right', [
        U.el('bcv-mark__chathead', [U.text('bcv-mark__chatk', 'Comments', 'span'), h('button', { type: 'button', class: 'bcv-sheet__close bcv-mark__fold', 'aria-label': 'Hide the comments', title: 'Hide the comments', onclick: () => unfold(false) }, U.svg('M15 6l-6 6 6 6', { size: 14, stroke: 'var(--bcv-ink2)', width: 2.3 }))]),
        built.chat,
      ]);
      right.inert = true;
      cols.append(left, right);
      return {
        cls: 'bcv-mark bcv-mark--side',
        kids: [cols],
        // (2.99.15) as small as what it holds: the taller of the two columns as they would stand on their own — the
        // thread measured by its bubbles, not by the column it stretches to fill
        measure: () => {
          const th = built.chat.querySelector('.bcv-mcard__thread'), ft = built.chat.querySelector('.bcv-mcard__foot');
          const kids = th ? [...th.children] : [];
          const threadH = kids.reduce((n, k) => n + k.offsetHeight, 0) + Math.max(0, kids.length - 1) * 12 + 18;
          const rightH = right.firstElementChild.offsetHeight + threadH + (ft?.offsetHeight || 0);
          const leftH = head.offsetHeight + built.el.offsetHeight;
          return (cols.classList.contains('is-folded') ? leftH : Math.max(leftH, rightH)) + 2;
        },
        glide: glideFrom ? [[valueEl, glideFrom]] : [],
      };
    }
    return {
      cls: 'bcv-mark',
      kids: [head, U.el('bcv-sheet__list bcv-mark__body', [built.el])],
      // (what it holds, measured — not the list, which stretches to the box and read as the whole box every time)
      measure: () => head.offsetHeight + built.el.offsetHeight + 2,
      glide: glideFrom ? [[valueEl, glideFrom]] : [],
    };
  }

  const courseVars = (c) => ({ '--bcv-cc': c.color || null, '--bcv-cc-ink': c.palette?.text || c.color || null });
  function openMarkBox(ctx, c, a, s, from, { hover = false, again = null, anchor = 'right', onClosed = null, within = null } = {}) {
    const scored = s.workflow_state === 'graded' && s.score !== null && s.score !== undefined;
    const posted = scored && s.posted_at !== null;
    const word = (sel) => from.querySelector(sel)?.textContent?.trim() || '';
    // the header's words are the button's own where it has them, so each glides from where it stands
    const value = posted ? store.fmtPts(s.score) : word('.bcv-detail__gradepc') || word('[data-glide="word"]') || (s.excused ? 'Excused' : 'Submitted');
    const label = posted ? `/ ${a.points_possible ?? '—'}` : ''; // (2.99.10: the share is the ring's, beside it)
    const when = word('.bcv-detail__gradewhen') || (s.submitted_at ? U.fmtAt(s.submitted_at) : '');
    const glideFrom = from.querySelector(posted ? '.bcv-detail__gradescore, [data-glide="score"]' : '.bcv-detail__gradepc, [data-glide="word"]');
    if (glideFrom && !glideFrom.dataset.glide) glideFrom.dataset.glide = 'g';
    const room = within?.isConnected ? within.getBoundingClientRect().width - 32 : innerWidth - 32;
    const side = room >= 640; // (sideways where there is room for it: the comments a column to the right)
    return growBox(ctx, from, { label: `${a.name}: ${posted ? 'your mark' : 'what you handed in'}`, W: 400, H: 560, anchor, hover, onClosed, within, vars: courseVars(c) }, (api) => markContent(ctx, c, a, s, api, { value, label, when, again: again ? () => again(api) : null, glideFrom, side }));
  }

  const D = {};

  /** "Mark as done", where Canvas asks for it: an assignment or a page that is a module item with a
   *  must_mark_done requirement has nothing to hand in, and the mark is the whole of the work. The
   *  button is the requirement's own state and flips it — pressed once more it takes the mark back,
   *  as Canvas's own does. Absent for everything else, because the call would do nothing. `type`
   *  is the asset's kind in the module (Assignment, Page); `noun` is what the title calls it. */
  function doneButton(ctx, c, a, item, { cls = 'bcv-btn', primary = false, type = 'Assignment', noun = 'assignment' } = {}) {
    const req = item?.completion_requirement;
    if (!item || req?.type !== 'must_mark_done') return null;
    // (Brightspace: a topic done by hand is marked on its own page — its API takes no such mark from the student — so
    // the button goes there, and once Brightspace has the mark it says Done)
    if (BCV.lms?.d2l) {
      const tick = U.svg(req.completed ? 'M20 6L9 17l-5-5' : 'M12 4a8 8 0 100 16 8 8 0 000-16z', { size: 14, stroke: 'currentColor', width: 2.2 });
      if (req.completed) return h('span', { class: `${cls} is-done`, title: `Marked as done on ${BCV.lms.name}` }, [tick, 'Done']);
      return h('a', { class: `${cls}${primary && cls === 'bcv-btn' ? ' bcv-btn--primary' : primary ? ' is-primary' : ''}`, href: BCV.lms.toPage(`/courses/${c.id}/modules/items/${item.id}?bcv=native`), title: `${BCV.lms.name} takes this mark on its own page for the ${noun}` }, [tick, `Mark done on ${BCV.lms.name}`]);
    }
    let done = !!req.completed;
    let busy = false;
    const btn = h('button', { type: 'button', class: cls, 'aria-pressed': String(done) });
    const paint = () => {
      btn.replaceChildren(U.svg(done ? 'M20 6L9 17l-5-5' : 'M12 4a8 8 0 100 16 8 8 0 000-16z', { size: 14, stroke: 'currentColor', width: 2.2 }), done ? 'Done' : 'Mark as done');
      btn.classList.toggle('is-done', done);
      btn.classList.toggle(cls === 'bcv-btn' ? 'bcv-btn--primary' : 'is-primary', primary && !done);
      btn.setAttribute('aria-pressed', String(done));
      btn.title = done ? 'Marked as done — press to take that back' : `Mark this ${noun} as done`;
    };
    btn.addEventListener('click', async () => {
      if (busy) return;
      busy = true;
      btn.disabled = true;
      const want = !done;
      try {
        await store.markItemDone(c.id, item.module_id, item.id, want, { type, assetId: a.id });
        done = want;
        paint();
      } catch (e) {
        U.toast(`${BCV.lms.name} did not take the mark: ${e?.message || e}`, { error: true });
      } finally {
        busy = false;
        btn.disabled = false;
      }
    });
    paint();
    return btn;
  }

  /** The assignments either side of this one, in the order the Assignments tab lists them, as a
   *  Previous / Next row: each names where it goes, and an edge with nothing beyond it keeps its
   *  side empty rather than letting the other button drift across. */
  function navRow(app, c, nav, cls = 'bcv-detail__nav') {
    if (!nav || (!nav.prev && !nav.next)) return null;
    const one = (x, dir) => (x ? h('button', {
      type: 'button', class: `${cls}btn ${cls}btn--${dir}`, title: x.name,
      onclick: () => app.go(x.href || `${c.url}/assignments/${x.id}`),
    }, [
      dir === 'prev' ? U.svg(IC.back, { size: 14, stroke: 'currentColor', width: 2.1 }) : null,
      U.el(`${cls}body`, [U.text(`${cls}kicker`, dir === 'prev' ? 'Previous' : 'Next', 'span'), U.text(`${cls}name bcv-ellip`, x.name, 'span')]),
      dir === 'next' ? U.svg(IC.chevron, { size: 14, stroke: 'currentColor', width: 2.1 }) : null,
    ]) : h('span', { class: `${cls}gap` }));
    return U.el(cls, [one(nav.prev, 'prev'), one(nav.next, 'next')]);
  }

  /** (2.99.13, every item from 2.99.17) The items either side, as one pill of two halves — each names where it goes. */
  function navPill(app, nav) {
    if (!nav || (!nav.prev && !nav.next)) return null;
    const one = (x, dir) => (x ? h('button', { type: 'button', class: `bcv-asg__navbtn bcv-asg__navbtn--${dir}`, title: `${dir === 'prev' ? 'Previous' : 'Next'}: ${x.name}`, onclick: () => app.go(x.href) }, [
      dir === 'prev' ? U.svg('M15 6l-6 6 6 6', { size: 13, stroke: 'currentColor', width: 2.4 }) : null,
      h('span', { class: 'bcv-ellip', text: dir === 'prev' ? 'Previous' : 'Next' }), // (2.99.52: just the word, the item's name on hover)
      dir === 'next' ? U.svg('M9 6l6 6-6 6', { size: 13, stroke: 'currentColor', width: 2.4 }) : null,
    ]) : null);
    return U.el('bcv-asg__nav', [one(nav.prev, 'prev'), one(nav.next, 'next')].filter(Boolean));
  }
  /** (2.99.17) The row along the top of any item: the way back, and the items either side (store.itemNeighbours — the
   *  modules' order when it was opened from them, else its own tab's), drawn when they land: the page never waits. */
  function itemTop(ctx, c, backEl, type, assetId, { kind = 'courses' } = {}) {
    const { app, route } = ctx;
    const itemId = route.params?.get?.('module_item_id') || null;
    const viaModules = !!itemId || /^\s*Modules\s*$/.test(backEl.textContent || '');
    const slot = h('span', { class: 'bcv-asg__navslot', hidden: '' });
    // (2.99.52) on the desktop the pill sits in the bar across the top, beside the search box and across from the widgets
    // (as the Mac's Previous / Next sit in its toolbar), and goes as the item is left; on the phone, in the item's own row
    const inBar = !BCV.phone?.active?.();
    store.itemNeighbours(c.id, type, assetId, { viaModules, itemId, kind, courseUrl: c.url })
      .then((nav) => {
        if (!ctx.alive() || !slot.parentNode) return;
        const el = navPill(app, nav);
        if (el && inBar) {
          slot.remove();
          let host = document.getElementById('bcv-topsearch');
          if (!host) { host = h('div', { id: 'bcv-topsearch', class: 'bcv-topsearch' }); document.body.append(host); }
          host.querySelectorAll('.bcv-asg__nav').forEach((x) => x.remove());
          el.classList.add('bcv-asg__nav--bar');
          host.append(el); // (taken away as the next screen is drawn: app.js)
        } else if (el) slot.replaceWith(el); else slot.remove();
      })
      .catch(() => slot.remove());
    return U.el('bcv-asg__top bcv-item__top', [backEl, h('span', { class: 'bcv-asg__spring' }), slot]);
  }

  D.assignment = async (ctx, shell) => {
    const { app, route } = ctx;
    const c = shell.course;
    const b = cols();
    const main = mainCol(), side = sideCol();
    b.append(main, side);
    main.append(U.loading());
    const [a, sub] = await Promise.all([store.assignment(c.id, route.arg).catch(() => null), store.submission(c.id, route.arg).catch(() => null)]);
    if (!ctx.alive()) return b;
    if (!a) return main.replaceChildren(U.errorBox('This assignment could not be loaded.')) || b;
    const s = sub || a.submission || {};
    // The item's place in its modules (Mark as done) and among the course's assignments (Previous /
    // Next) are asked for now and drawn when they land: the page never waits for them. The course's
    // assignment list is the slow one — every assignment with its submission — and behind a
    // dashboard's own requests it took the page past its patience, which reads as a page that never
    // comes. Each slot is filled, or taken out, when the answer arrives.
    // (the phone's Previous / Next row walks the same order as the desktop's pill: the modules when opened from them)
    const viaModules = !!route.params.get('module_item_id') || /^\s*Modules\s*$/.test(String(app.backTo?.({ label: '' })?.label || ''));
    const later = Promise.all([store.moduleItemFor(c.id, 'Assignment', route.arg, { asset: a, itemId: route.params.get('module_item_id') }).catch(() => null),
      store.itemNeighbours(c.id, 'Assignment', a.id, { viaModules, itemId: route.params.get('module_item_id'), courseUrl: c.url }).catch(() => null)])
      .then(([modItem, nav]) => ({ modItem, nav }));
    const slot = (cls) => h('span', { class: cls, hidden: '' });
    // (the slot has a parent from the moment it is drawn, before the screen is in the document)
    const fill = (el, make) => later.then((x) => { if (!ctx.alive() || !el.parentNode) return; const made = make(x); if (made) el.replaceWith(made); else el.remove(); });
    const types = (a.submission_types || []).map((t) => ({ online_upload: 'a file upload', online_text_entry: 'a text entry', online_url: 'a website URL', media_recording: 'a media recording', discussion_topic: 'a discussion post', online_quiz: 'a quiz', external_tool: 'an external tool', on_paper: 'on paper', none: 'nothing to submit', student_annotation: 'an annotation' }[t] || t)).join(', ');
    shell.reader = { title: a.name, html: a.description || '' };
    const isTool = (a.submission_types || []).includes('external_tool');
    const toolAttrs = a.external_tool_tag_attributes || {};
    const toolNewTab = !!toolAttrs.new_tab;
    // Canvas's own launch route for an assignment's tool (same URL its assignment page embeds).
    const toolLaunch = toolAttrs.url ? `${c.url}/external_tools/retrieve?assignment_id=${a.id}&display=borderless&url=${encodeURIComponent(toolAttrs.url)}` : `${c.url}/assignments/${a.id}`;
    let startPoll = () => {};
    /** The tool, full screen over the page (a new tab only where there is no popup to be had); the grade is looked for once it is open. */
    const launchTool = (from = null) => { if (BCV.exttool) { BCV.exttool.open({ title: a.name, url: toolLaunch, newTab: toolLaunch, from }); startPoll(); } else window.open(toolLaunch, '_blank', 'noopener'); };
    const available = a.unlock_at && a.lock_at ? `${U.fmtAt(a.unlock_at)} – ${U.fmtAt(a.lock_at)}` : a.unlock_at ? `from ${U.fmtAt(a.unlock_at)}` : a.lock_at ? `until ${U.fmtAt(a.lock_at)}` : null;
    // Our own submission flow handles uploads, text entries and URLs (plus the tools Canvas
    // lists for handing work in); media recordings and annotations stay on Canvas's page.
    const nativeSubmit = (a.submission_types || []).some((t) => ['online_upload', 'online_text_entry', 'online_url'].includes(t));
    const canvasOnly = !nativeSubmit && (a.submission_types || []).some((t) => ['media_recording', 'student_annotation'].includes(t));
    const attemptsLeft = !(a.allowed_attempts > 0) || (s.attempt || 0) < a.allowed_attempts + (s.extra_attempts || 0); // (extra attempts the teacher granted count)
    const statusOf = (x) => (x.excused ? 'Excused' : x.workflow_state === 'graded' ? 'Graded' : x.submitted_at ? (x.late ? 'Submitted late' : 'Submitted') : x.missing ? 'Missing' : 'Not submitted');
    const gradedOf = (x) => x.workflow_state === 'graded' && x.score !== null && x.score !== undefined;
    // Canvas posts a grade separately from marking it: posted_at === null means the instructor is
    // holding it back, and a number shown then is a number the student is not supposed to have.
    const postedOf = (x) => gradedOf(x) && x.posted_at !== null;
    const heldOf = (x) => gradedOf(x) && x.posted_at === null;
    const status = statusOf(s), posted = postedOf(s), held = heldOf(s);
    const feedback = (s.submission_comments || []).length || Object.keys(s.rubric_assessment || {}).length;
    // the phone draws the item page its own way (the iPhone mockup)
    if (BCV.phone?.active()) return BCV.phone.assignment(ctx, shell, { a, s, types, available, isTool, toolNewTab, toolLaunch, nativeSubmit, canvasOnly, attemptsLeft, status, posted, held, slot, fill });
    // (2.99.13) The assignment page, to the mockup: the way back and the assignments either side on one row; the title and
    // one pill saying where the work stands; the facts in boxes (due, points, how it is handed in, the rubric); then the
    // one big pill — Submit assignment, which grows into the hand-in panel; once marked, the grade, which grows into the
    // mark's card (Submit again inside it) — and the instructions. Every fact is said once.
    const fromTodo = route.params.get('from') === 'todo';
    const back = fromTodo ? { href: '/#todo', label: 'To Do' } : app.backTo ? app.backTo({ href: `${c.url}/assignments`, label: 'Assignments' }) : { href: `${c.url}/assignments`, label: 'Assignments' };
    app.nameHere?.(a.name); // the next screen's Back names this assignment
    if (a.rubric?.length) BCV.lazy?.load?.('rubric').catch(() => {}); // (the rubric's box wears the rubric's own ring, and so does the mark's card)
    if (!BCV.phone?.active?.()) BCV.lazy?.load?.('submit').catch(() => {});
    // the hand-in panel, built once with the page (files attached and a comment written outlive its being closed)
    const hooks = { close: null, layout: null, done: null };
    const handScreen = nativeSubmit && !isTool ? await BCV.screens.submit.render(ctx, c, { pop: hooks, a, sub: s, back }) : null;
    if (!ctx.alive()) return b;
    const D0 = (v) => U.parse(v);
    const longAt = (d) => `${U.DAYS[d.getDay()]}, ${U.fmtShort(d)} at ${U.fmtTime(d)}`;
    const shortAt = (d) => `${U.fmtShort(d)}, ${U.fmtTime(d)}`;
    const due = D0(a.due_at), lockAt = D0(a.lock_at), unlockAt = D0(a.unlock_at);
    const pct = (x) => (postedOf(x) && Number(a.points_possible) > 0 ? Math.round((Number(x.score) / Number(a.points_possible)) * 100) : null);
    const toneOf = (p) => (p === null ? 'green' : p >= 80 ? 'green' : p >= 60 ? 'orange' : 'red');
    const used = (x) => Number(x.attempt) || 0;
    const allowed = a.allowed_attempts > 0 ? a.allowed_attempts + (s.extra_attempts || 0) : 0;
    const closedNow = () => !!lockAt && lockAt < Date.now();
    const lockedNow = () => !closedNow() && (!!(unlockAt && unlockAt > Date.now()) || !!a.locked_for_user);
    const leftOf = (x) => !allowed || used(x) < allowed;
    const canAgain = (x) => !!handScreen && a.can_submit !== false && !closedNow() && !lockedNow() && leftOf(x);
    const SHORT = { online_upload: 'file', online_text_entry: 'text', online_url: 'link', media_recording: 'media', student_annotation: 'annotation', external_tool: 'external tool', online_quiz: 'quiz', discussion_topic: 'discussion post', on_paper: 'on paper', none: 'nothing' };
    const LONE = { online_upload: 'File upload', online_text_entry: 'Text entry', online_url: 'Website URL', media_recording: 'Media recording', student_annotation: 'Annotation', external_tool: 'External tool', online_quiz: 'Online quiz', discussion_topic: 'Discussion', on_paper: 'On paper', none: 'Nothing to hand in' };
    const typeList = (a.submission_types || []).filter((t) => t !== 'not_graded');
    const order = ['online_text_entry', 'online_upload', 'online_url', 'media_recording', 'student_annotation', 'external_tool', 'online_quiz', 'discussion_topic', 'on_paper', 'none'];
    const sortedTypes = typeList.slice().sort((x, y) => order.indexOf(x) - order.indexOf(y));
    const howWord = sortedTypes.length === 1 ? (LONE[sortedTypes[0]] || sortedTypes[0]) : (() => { const w = sortedTypes.map((t) => SHORT[t] || t); const j = w.length <= 1 ? w.join('') : `${w.slice(0, -1).join(', ')} or ${w[w.length - 1]}`; return j.charAt(0).toUpperCase() + j.slice(1); })() || 'Nothing to hand in';
    const attemptsWord = (x) => (allowed ? (used(x) ? `${used(x)} of ${U.plural(allowed, 'attempt')} used` : `${U.plural(allowed, 'attempt')} allowed`) : used(x) ? `${U.plural(used(x), 'attempt')} used` : 'Unlimited attempts');
    const handsIn = nativeSubmit || isTool || canvasOnly || typeList.some((t) => t === 'online_quiz' || t === 'discussion_topic');

    /** Where the work stands, in one pill: { text, tone, icon }. */
    function standingOf(x) {
      const CLOCK = 'M12 4a8 8 0 100 16 8 8 0 000-16zM12 8v4l3 2', LOCK = 'M6 11h12v9H6zM8.5 11V8a3.5 3.5 0 017 0v3', TICK = 'M20 6L9 17l-5-5', WARN = 'M12 7v6M12 17h.01M12 3a9 9 0 100 18 9 9 0 000-18z';
      if (x.excused) return { text: 'Excused', tone: 'grey', icon: TICK };
      if (postedOf(x)) return { text: `Graded · ${x.graded_at ? longAt(D0(x.graded_at)) : 'marked'}`, tone: 'green', icon: TICK };
      if (heldOf(x)) return { text: 'Submitted · grade not released yet', tone: 'green', icon: TICK };
      if (x.submitted_at) return { text: `Submitted${x.late ? ' late' : ''} · ${longAt(D0(x.submitted_at))}`, tone: x.late ? 'orange' : 'green', icon: TICK };
      if (closedNow()) return { text: `Closed · ${handsIn ? 'no submission' : U.fmtShort(lockAt)}`, tone: handsIn ? 'red' : 'grey', icon: WARN };
      if (unlockAt && unlockAt > Date.now()) return { text: `Locked · opens ${U.DAYS[unlockAt.getDay()]}, ${U.fmtShort(unlockAt)}`, tone: 'orange', icon: LOCK };
      if (a.locked_for_user) return { text: `Locked${a.lock_explanation ? ` · ${htmlToText(a.lock_explanation, 90)}` : ''}`, tone: 'orange', icon: LOCK };
      if (!handsIn) return { text: due ? `${LONE[typeList[0]] || 'Nothing to hand in'} · due ${longAt(due)}` : (LONE[typeList[0]] || 'Nothing to hand in'), tone: 'grey', icon: CLOCK };
      if (due && due < Date.now()) return { text: `${x.missing ? 'Missing' : 'Past due'} · ${lockAt ? `open until ${shortAt(lockAt)}` : `was due ${shortAt(due)}`}`, tone: x.missing ? 'red' : 'orange', icon: WARN };
      return { text: due ? `Open · due ${longAt(due)}` : 'Open · no due date', tone: 'blue', icon: CLOCK };
    }
    // (2.99.53) the word alone, as the Mac's chip says it (the when is in Your Work and the facts)
    const statusEl = (x) => { const st = standingOf(x); return h('div', { class: `bcv-asg__status is-${st.tone}`, title: st.text }, [U.text('bcv-asg__statustext', st.text.split(' · ')[0], 'span')]); };

    const GRADING = { points: 'Graded', pass_fail: 'Complete or incomplete', letter_grade: 'Letter grade', gpa_scale: 'GPA scale', percent: 'Percentage', not_graded: 'Not graded' };
    // (2.99.50) each fact led by its own tile, as the Mac's details are (Due, Points, Handed in as)
    const FACT_ICON = { Due: IC.cal, Points: IC.star, Available: 'M12 4a8 8 0 100 16 8 8 0 000-16zM12 8v4l3 2', 'Handed In As': 'M12 15V4M8 8l4-4 4 4M5 14v4a2 2 0 002 2h10a2 2 0 002-2v-4', Attempts: 'M4 12a8 8 0 108-8 8 8 0 00-5.7 2.3L4 8.6M4 4v4.6h4.6', 'Submit as': 'M12 15V4M8 8l4-4 4 4M5 14v4a2 2 0 002 2h10a2 2 0 002-2v-4', Rubric: IC.check };
    const fact = (k, v, sub, extra = {}) => h(extra.onclick ? 'button' : 'div', { class: `bcv-asg__fact ${extra.cls || ''}`, type: extra.onclick ? 'button' : null, onclick: extra.onclick || null, title: extra.title || null }, [
      FACT_ICON[k] ? h('span', { class: 'bcv-asg__facttile', 'aria-hidden': 'true' }, U.svg(FACT_ICON[k], { size: 15, stroke: 'currentColor', width: 2 })) : null,
      U.el('bcv-asg__facttext', [U.text('bcv-asg__factk', k, 'span'), U.text('bcv-asg__factv bcv-ellip', v, 'span'), sub ? U.text('bcv-asg__factsub bcv-ellip', sub, 'span') : null]),
      extra.side || null,
    ]);
    const dueSub = lockAt && due && lockAt > due ? `Late until ${U.fmtShort(lockAt)}` : lockAt && due && +lockAt === +due ? 'Closes when due' : unlockAt && unlockAt > Date.now() ? `Opens ${U.fmtShort(unlockAt)}` : lockAt && !due ? `Closes ${U.fmtShort(lockAt)}` : due ? 'No late cutoff' : 'Hand in any time';
    const rubricFact = () => {
      if (!a.rubric?.length) return null;
      const ringSlot = h('span', { class: 'bcv-asg__factring', 'aria-hidden': 'true' });
      const drawRing = () => { const r = BCV.rubricRing?.miniRing?.(a, s, { size: 38 }); if (r) ringSlot.replaceChildren(r); };
      if (BCV.rubricRing) drawRing(); else BCV.lazy?.load?.('rubric').then(drawRing).catch(() => {});
      const worth = a.rubric.reduce((n, cr) => n + (Number(cr.points) || 0), 0);
      return fact('Rubric', U.plural(a.rubric.length, 'criterion', 'criteria'), worth ? `${store.fmtPts(worth)} pts` : null, { cls: 'bcv-asg__fact--rubric', onclick: () => CS().openRubric(a, s), title: 'How it is marked', side: ringSlot });
    };
    // (2.99.53) the Mac's facts, one to a line: Due, Points, Available, Handed In As, Attempts
    const macAt = (d) => `${U.fmtShort(d)} at ${U.fmtTime(d)}`;
    const availWord = [unlockAt ? `Opens ${macAt(unlockAt)}` : null, lockAt ? `Closes ${macAt(lockAt)}` : null].filter(Boolean).join(' · ');
    const factsEl = (x) => U.el('bcv-asg__facts', [
      fact('Due', due ? macAt(due) : 'No due date', null),
      a.points_possible !== null && a.points_possible !== undefined ? fact('Points', `${store.fmtPts(a.points_possible)} pts`, null) : null,
      availWord ? fact('Available', availWord, null) : null,
      fact('Handed In As', howWord, null),
      handsIn && !typeList.some((t) => t === 'on_paper' || t === 'none') ? fact('Attempts', attemptsWord(x), null) : null,
    ]);

    // ---- the one big pill ----------------------------------------------------------------------------------------
    const ICONS = { up: 'M12 19V5M6 11l6-6 6 6', lock: 'M6 11h12v9H6zM8.5 11V8a3.5 3.5 0 017 0v3', tick: 'M20 6L9 17l-5-5', x: 'M6 6l12 12M18 6L6 18', play: 'M8 5v14l11-7z', out: 'M9 6h9v9M18 6L7 17' };
    let current = s; // (the submission as the page knows it: a tool's grade landing, a hand-in sent, redraws from it)
    let opened = null; // (the box open off the pill, if any)
    const openHand = (pill, api = null) => {
      if (!handScreen) return;
      const content = (bx) => {
        hooks.close = bx.close;
        hooks.layout = () => bx.relayout();
        hooks.done = () => { setTimeout(() => { if (!bx.folding()) bx.close(); }, 1300); sentOff = true; };
        return { cls: 'bcv-hand', kids: [handScreen], measure: () => handScreen.scrollHeight + 2, after: () => setTimeout(() => (handScreen.querySelector('.bcv-sb__tab.is-active, .bcv-sb__tab, .bcv-sb__drop, .bcv-sb__ta') || handScreen).focus?.({ preventScroll: true }), 60) };
      };
      if (api) { api.swap(content, { W: 400 }); return; }
      opened = growBox(ctx, pill, { label: `${a.name}: hand in`, W: 380, H: 600, anchor: 'center', within: pill.closest('.bcv-asg__card'), onClosed: afterClose, vars: courseVars(c) }, content);
    };
    let sentOff = false;
    const afterClose = () => { if (sentOff && ctx.alive()) app.go(`${c.url}/assignments/${a.id}`, { confirmed: true }); }; // (handed in: the page redraws, Submitted)
    /** (2.99.16) How the class did, as a box plot beside the grade: the whiskers from the lowest mark to the highest, the
     *  box the middle half (lower to upper quartile), a tick at the median, a ring at the mean, and a dot for this student
     *  in their grade's tone. The numbers are there on a hover (or the arrow keys): the mark nearest the pointer lights
     *  and a readout over it says what it is; for a screen reader, all of them at once. */
    function classPlot(st, mine, possible, tone) {
      const num = (v) => (v === null || v === undefined || v === '' || !Number.isFinite(Number(v)) ? null : Number(v));
      if (!st || num(st.mean) === null || num(st.max) === null) return null;
      const lo = num(st.min) ?? 0, hi = num(st.max), q1 = num(st.lower_q), md = num(st.median), q3 = num(st.upper_q), mean = num(st.mean), me = num(mine);
      const P = num(possible) > 0 ? num(possible) : Math.max(hi, me ?? 0, 1);
      const at = (v) => Math.max(0, Math.min(1, v / P));
      const pct = (v) => `${(at(v) * 100).toFixed(2)}%`;
      const marks = [['Low', lo, 'lo'], ['Lower quartile', q1, 'q1'], ['Median', md, 'md'], ['Upper quartile', q3, 'q3'], ['High', hi, 'hi'], ['Mean', mean, 'mean'], ['You', me, 'me']].filter(([, v]) => v !== null);
      const mk = (cls, style) => h('span', { class: `bcv-cplot__${cls}`, style, 'aria-hidden': 'true' });
      const tip = h('span', { class: 'bcv-cplot__tip', 'aria-hidden': 'true' }, [h('span', { class: 'bcv-cplot__tipk' }), h('span', { class: 'bcv-cplot__tipv' })]);
      const els = {
        lo: mk('cap', { left: pct(lo) }), hi: mk('cap', { left: pct(hi) }),
        md: md !== null ? mk('median', { left: pct(md) }) : null,
        mean: mk('mean', { left: pct(mean) }),
        me: me !== null ? mk('me', { left: pct(me), '--bcv-cplot-tone': `var(--bcv-${tone})` }) : null,
      };
      const plot = h('span', { class: 'bcv-cplot__plot' }, [
        mk('track'),
        mk('whisker', { left: pct(lo), width: `${((at(hi) - at(lo)) * 100).toFixed(2)}%` }),
        q1 !== null && q3 !== null ? (els.box = mk('box', { left: pct(q1), width: `${Math.max(0.5, (at(q3) - at(q1)) * 100).toFixed(2)}%` })) : null,
        els.lo, els.hi, els.md, els.mean, els.me, tip,
      ]);
      const said = marks.map(([k, v]) => `${k} ${store.fmtPts(v)}`).join(', ');
      const wrap = h('span', { class: 'bcv-cplot', tabindex: '0', role: 'img', 'aria-label': `How the class did, out of ${store.fmtPts(P)}: ${said}`, title: '' }, [h('span', { class: 'bcv-cplot__k', text: 'Class' }), plot]);
      let on = -1;
      const show = (i) => {
        on = i;
        for (const el of plot.querySelectorAll('.is-lit')) el.classList.remove('is-lit');
        if (i < 0) { wrap.classList.remove('is-reading'); return; }
        const [k, v, key] = marks[i];
        const lit = key === 'q1' || key === 'q3' ? els.box : els[key];
        lit?.classList.add('is-lit');
        tip.firstChild.textContent = k;
        tip.lastChild.textContent = store.fmtPts(v);
        tip.style.setProperty('--x', `${(at(v) * plot.getBoundingClientRect().width).toFixed(1)}px`); // (moved by transform: it glides from mark to mark without a layout each frame)
        wrap.classList.add('is-reading');
      };
      const nearest = (clientX) => {
        const r = plot.getBoundingClientRect(), f = (clientX - r.left) / Math.max(1, r.width);
        let best = 0, d = Infinity;
        // (marks at one place: the student's own first, then the summary in its order)
        const order = marks.map((m, i) => i).sort((x, y) => (marks[y][2] === 'me') - (marks[x][2] === 'me'));
        for (const i of order) { const dd = Math.abs(at(marks[i][1]) - f); if (dd < d - 1e-6) { d = dd; best = i; } }
        return best;
      };
      wrap.addEventListener('pointermove', (e) => { if (e.pointerType === 'mouse' || e.pointerType === 'pen') show(nearest(e.clientX)); });
      wrap.addEventListener('pointerdown', (e) => show(nearest(e.clientX)));
      wrap.addEventListener('pointerleave', () => { if (document.activeElement !== wrap) show(-1); });
      wrap.addEventListener('focus', () => show(Math.max(0, marks.findIndex(([, , key]) => key === 'me'))));
      wrap.addEventListener('blur', () => show(-1));
      wrap.addEventListener('keydown', (e) => {
        const byPlace = marks.map((m, i) => i).sort((x, y) => marks[x][1] - marks[y][1]);
        const at0 = byPlace.indexOf(on);
        if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') { e.preventDefault(); show(byPlace[Math.max(0, Math.min(byPlace.length - 1, (at0 < 0 ? 0 : at0) + (e.key === 'ArrowRight' ? 1 : -1)))]); }
        else if (e.key === 'Escape' && on >= 0) { e.stopPropagation(); show(-1); }
      });
      return wrap;
    }
    /** The pill and what sits beside it, for the submission `x`. */
    function actionEl(x) {
      const p = pct(x);
      const pill = (kind, label, icon, { off = false, onClick = null, kids = null, title = null, cls = '' } = {}) => h('button', {
        type: 'button', class: `bcv-asg__pill is-${kind} ${cls}`, disabled: off || null, 'aria-disabled': off ? 'true' : null, title,
        'aria-haspopup': onClick && (kind === 'go' || kind === 'graded' || kind === 'done') ? 'dialog' : null, 'aria-expanded': onClick && (kind === 'go' || kind === 'graded' || kind === 'done') ? 'false' : null,
        onclick: onClick ? (e) => onClick(e.currentTarget) : null,
      }, kids || [U.svg(icon, { size: 15, stroke: 'currentColor', width: 2.4 }), h('span', { 'data-glide': 'word', text: label })]);
      const note = (...bits) => { const t = bits.filter(Boolean); return t.length ? U.el('bcv-asg__note', t.map((v) => (typeof v === 'string' ? U.text('bcv-asg__notetext', v, 'span') : v))) : null; };
      const againLink = canAgain(x) ? h('button', { type: 'button', class: 'bcv-asg__again', text: 'Submit again', onclick: () => openHand(row.querySelector('.bcv-asg__pill')) }) : null;
      const extras = [
        a.quiz_id && !typeList.includes('online_quiz') ? U.btn('Open quiz', { icon: IC.bolt, onClick: () => app.go(`${c.url}/quizzes/${a.quiz_id}`) }) : null,
        (() => { const el = slot('bcv-detail__doneslot'); fill(el, ({ modItem }) => doneButton(ctx, c, a, modItem, { primary: !handsIn })); return el; })(),
      ];
      let main = null, aside = null;
      const comments = (x.submission_comments || []).length;
      if (postedOf(x)) {
        const letter = a.grading_type && a.grading_type !== 'points' && x.grade !== null && x.grade !== undefined ? String(x.grade) : null;
        main = pill('graded', '', null, { cls: `is-${toneOf(p)}`, title: 'Your grade: feedback, attempts and comments', onClick: (el) => openFeedback(current, el), kids: [
          U.svg(ICONS.tick, { size: 15, stroke: 'currentColor', width: 2.5 }),
          h('span', { class: 'bcv-asg__score', 'data-glide': 'score', text: store.fmtPts(x.score) }),
          h('span', { class: 'bcv-asg__of', text: `/ ${a.points_possible ?? '—'}` }),
          letter || p !== null ? h('span', { class: 'bcv-asg__pct', text: letter || `${p}%` }) : null,
        ] });
        aside = note(x.late ? `Late${x.points_deducted ? ` · −${store.fmtPts(x.points_deducted)} pts` : ''}` : null, comments ? U.plural(comments, 'comment') : null, classPlot(a.score_statistics, x.score, a.points_possible, toneOf(p)));
      } else if (heldOf(x) || x.submitted_at || x.excused) {
        const word = x.excused ? 'Excused' : heldOf(x) ? 'Submitted' : x.late ? 'Submitted late' : 'Submitted';
        main = pill('done', word, ICONS.tick, { cls: x.excused ? 'is-grey' : '', title: 'What you handed in, and comments', onClick: x.submitted_at || heldOf(x) ? (el) => openFeedback(current, el) : null, off: !(x.submitted_at || heldOf(x)) });
        aside = note(againLink, heldOf(x) ? 'Grade not released yet' : x.submitted_at ? longAt(D0(x.submitted_at)) : null, comments ? U.plural(comments, 'comment') : null);
      } else if (closedNow() && handsIn) {
        main = pill('off', `Closed ${U.fmtShort(lockAt)}`, ICONS.x, { off: true, title: `Closed ${U.fmtAt(lockAt)}: nothing can be handed in now` });
      } else if (lockedNow() && handsIn) {
        main = pill('off', unlockAt && unlockAt > Date.now() ? `Opens ${U.fmtShort(unlockAt)}` : 'Locked', ICONS.lock, { off: true, title: a.lock_explanation ? htmlToText(a.lock_explanation, 160) : 'Locked' });
      } else if (isTool) {
        main = pill('go', used(x) > 0 ? 'Continue assignment' : 'Start assignment', ICONS.play, { onClick: (el) => launchTool(el), cls: 'bcv-detail__tool' });
        aside = note('Opens the assignment’s tool');
      } else if (nativeSubmit) {
        main = a.can_submit === false ? pill('off', 'Submissions closed', ICONS.x, { off: true }) : !leftOf(x) ? pill('off', 'No attempts left', ICONS.x, { off: true }) : pill('go', 'Submit assignment', ICONS.up, { onClick: (el) => openHand(el) });
        aside = note([howWord.toLowerCase().replace(/^./, (m) => m.toUpperCase()), attemptsWord(x).replace(/^U/, 'u')].join(' · '));
      } else if (typeList.includes('online_quiz') && a.quiz_id) {
        main = pill('go', 'Open quiz', IC.bolt, { onClick: () => app.go(`${c.url}/quizzes/${a.quiz_id}`) });
      } else if (typeList.includes('discussion_topic') && a.discussion_topic?.id) {
        main = pill('go', 'Open discussion', IC.disc, { onClick: () => app.go(`${c.url}/discussion_topics/${a.discussion_topic.id}`) });
      } else if (canvasOnly) {
        main = pill('go', 'Submit in Canvas', ICONS.out, { onClick: () => app.go(nativeHref(`${c.url}/assignments/${a.id}`)) });
        aside = note('Media and annotations are handed in on Canvas’s own page');
      }
      // (2.99.52) no card on a hover any more: the grade, or what was handed in, opens the Feedback sheet when pressed
      const row = U.el('bcv-asg__action', [main, aside, ...extras]);
      return row;
    }

    // ---- the row along the top: the way back, and the assignments either side --------------------------------------
    const backEl = h('button', { type: 'button', class: 'bcv-linkbtn bcv-detail__back bcv-asg__back', title: back.label, onclick: () => { app.markBack?.(); app.go(back.href); } }, [U.svg('M15 5l-7 7 7 7', { size: 15, stroke: 'currentColor', width: 2.4 }), h('span', { class: 'bcv-detail__backlabel bcv-ellip', text: back.label })]);
    const top = itemTop(ctx, c, backEl, 'Assignment', a.id);
    const titleEl = h('h1', { class: 'bcv-detail__title bcv-asg__title bcv-pretty', text: a.name });
    // (2.99.50) The Mac's assignment page: over the title its kind, its course and where it stands as chips; then the
    // instructions and the comments down the left, and down the right the one big action, the grade (its ring, the
    // score, how the class did) and the details — Due, Points, Handed in as, the rubric — each led by its tile.
    const kindOf = isTool ? ['Tool', IC.tool] : typeList.includes('online_quiz') ? ['Quiz', IC.bolt] : typeList.includes('discussion_topic') ? ['Discussion', IC.disc] : ['Assignment', IC.doc];
    const kindChip = h('span', { class: 'bcv-asg__kind' }, [h('span', { class: 'bcv-asg__kindtile', 'aria-hidden': 'true' }, U.svg(kindOf[1], { size: 14, stroke: 'currentColor', width: 2 })), h('span', { text: kindOf[0] })]);
    const courseChip = h('span', { class: 'bcv-asg__coursechip bcv-ellip', text: c.shortName || c.name || '' });
    let statusNow = statusEl(s);
    const chipsEl = U.el('bcv-asg__chips', [kindChip, courseChip, statusNow]);
    const headEl = U.el('bcv-detail__head bcv-asg__head', [chipsEl, titleEl, a.rubric?.length && !postedOf(s) ? U.el('bcv-asg__headrubric', [rubricFact()]) : null].filter(Boolean));
    const section = (title, extra, body) => U.el('bcv-asg__section', [U.el('bcv-asg__sectionhead', [h('h2', { class: 'bcv-asg__h2', text: title }), extra || null]), body]);
    const desc = a.description && htmlToText(a.description, 40).trim() ? CS().prose(a.description, { cls: 'bcv-asg__prose' }) : (isTool ? null : U.text('bcv-asg__quiet', 'No instructions were given.'));
    const instructions = desc ? section('Instructions', null, U.el('bcv-asg__panel', [desc])) : null;

    // the comments on the submission, and one to add (the thread the teacher's feedback comes back on)
    const commentsBody = U.el('bcv-asg__panel bcv-asg__comments');
    const commentCount = h('span', { class: 'bcv-asg__count', text: (s.submission_comments || []).length ? String((s.submission_comments || []).length) : '' });
    const drawComments = (x) => {
      const list = (x.submission_comments || []).slice().sort((p, q) => String(p.created_at).localeCompare(String(q.created_at)));
      commentCount.textContent = list.length ? String(list.length) : '';
      if (!list.length) { commentsBody.replaceChildren(h('div', { class: 'bcv-asg__nocomments' }, [U.svg(IC.disc, { size: 15, stroke: 'currentColor', width: 2 }), h('span', { text: 'No comments yet.' })])); return; }
      // (2.99.53) as the Mac's: the photo, the name, the attempt it was left on, the when at the right; the words under them
      commentsBody.replaceChildren(...list.map((cm) => U.el('bcv-asg__comment', [
        U.avatar(cm.author?.avatar_image_url || cm.avatar_path || null, cm.author_name || cm.author?.display_name || '', 34),
        U.el('bcv-asg__commentbody', [
          U.el('bcv-asg__commenthead', [U.text('bcv-asg__commentwho', cm.author_name || cm.author?.display_name || 'Someone', 'span'), cm.attempt ? U.text('bcv-asg__commentatt', `Attempt ${cm.attempt}`, 'span') : null, cm.created_at ? U.text('bcv-asg__commentwhen', U.fmtAt(cm.created_at), 'span') : null]),
          U.text('bcv-asg__commenttext', String(cm.comment || '').trim(), 'div'),
        ]),
      ])));
    };
    drawComments(s);
    const composer = U.el('bcv-asg__composer', [], { hidden: '' });
    const addBtn = h('button', { type: 'button', class: 'bcv-asg__addcomment' }, [U.svg(IC.disc, { size: 14, stroke: 'currentColor', width: 2 }), h('span', { text: 'Add a Comment' })]);
    addBtn.onclick = () => {
      if (!composer.hidden) { composer.querySelector('textarea')?.focus(); return; }
      const box = h('textarea', { class: 'bcv-textarea bcv-asg__composerbox', rows: 3, placeholder: 'Write a comment for your teacher…' });
      const send = h('button', { type: 'button', class: 'bcv-btn bcv-btn--primary', text: 'Send' });
      const cancel = h('button', { type: 'button', class: 'bcv-btn', text: 'Cancel', onclick: () => { composer.hidden = true; composer.replaceChildren(); } });
      send.onclick = async () => {
        if (!box.value.trim()) { box.focus(); return; }
        send.disabled = true; send.textContent = 'Sending…';
        try {
          // (pinned to the latest attempt: the history's highest, the submission's own number when it has none)
          const latest = Math.max(Number(current.attempt) || 0, ...(current.submission_history || []).map((h0) => Number(h0.attempt) || 0));
          await store.commentOnSubmission(c.id, a.id, box.value, latest || null);
          const fresh = await store.submission(c.id, route.arg, { force: true }).catch(() => null);
          if (!ctx.alive()) return;
          if (fresh) { current = fresh; drawComments(fresh); }
          composer.hidden = true; composer.replaceChildren();
        } catch (e) { send.disabled = false; send.textContent = 'Send'; U.toast?.(e?.message || 'The comment could not be sent.'); }
      };
      composer.replaceChildren(box, U.el('bcv-asg__composerrow', [cancel, send]));
      composer.hidden = false;
      box.focus();
    };
    const comments = section('Comments', U.el('bcv-asg__headtools', [commentCount, addBtn]), U.el('bcv-asg__commentwrap', [composer, commentsBody]));

    // (2.99.51) what was handed in, as the Mac's Your Work: when, then each file (Preview opens it in the viewer), the
    // text written or the web address given
    const workBody = U.el('bcv-asg__panel bcv-asg__work');
    const workSection = section('Your Work', null, workBody);
    const drawWork = (x) => {
      const files = x.attachments || [];
      const parts = [];
      // (2.99.53) the Mac's seal, filled, and the when as the Mac says it
      if (x.submitted_at) parts.push(h('div', { class: `bcv-asg__worksent${x.late ? ' is-late' : ''}` }, [h('span', { class: 'bcv-asg__seal', 'aria-hidden': 'true', html: '<svg viewBox="0 0 24 24" width="20" height="20"><path fill="currentColor" d="M12 1.6l2.4 1.8 3-.2.9 2.9 2.5 1.7-.9 2.9.9 2.9-2.5 1.7-.9 2.9-3-.2L12 22.4 9.6 20.6l-3 .2-.9-2.9-2.5-1.7.9-2.9-.9-2.9 2.5-1.7.9-2.9 3 .2z"/><path d="M8 12.2l2.7 2.7L16.2 9.4" fill="none" stroke="#fff" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>' }), h('span', { text: `Submitted ${macAt(D0(x.submitted_at))}${x.late ? ' · late' : ''}` })]));
      for (const f of files) parts.push(h('div', { class: 'bcv-asg__workfile' }, [
        h('span', { class: 'bcv-asg__workicon', 'aria-hidden': 'true', html: '<svg viewBox="0 0 32 40" width="26" height="32"><path d="M3 3a3 3 0 013-3h14l9 9v28a3 3 0 01-3 3H6a3 3 0 01-3-3z" fill="#f2f2f7"/><path d="M20 0v6a3 3 0 003 3h6" fill="#d1d1d6"/><path d="M9 17h14M9 21h14M9 25h10" stroke="#aeaeb2" stroke-width="1.6" stroke-linecap="round"/></svg>' }),
        U.text('bcv-asg__workname bcv-ellip', f.display_name || f.filename || 'File', 'span'),
        h('button', { type: 'button', class: 'bcv-asg__workprev' }, [U.svg('M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12zM12 9a3 3 0 100 6 3 3 0 000-6z', { size: 14, stroke: 'currentColor', width: 2 }), h('span', { text: 'Preview' })]),
      ]));
      if (x.body && htmlToText(x.body, 40).trim()) parts.push(U.el('bcv-asg__worktext', [CS().prose(x.body, { cls: 'bcv-asg__prose' })]));
      if (x.url && x.submission_type === 'online_url') parts.push(h('a', { class: 'bcv-asg__worklink bcv-ellip', href: x.url, target: '_blank', rel: 'noopener', text: x.url }));
      workBody.replaceChildren(...parts);
      // each Preview to its own file
      [...workBody.querySelectorAll('.bcv-asg__workprev')].forEach((b, i) => { b.onclick = () => BCV.viewer?.open(files[i], { canvas: true, from: b }); });
      workSection.hidden = !parts.length;
    };
    drawWork(s);

    // (2.99.51) See Feedback, as the Mac's: a sheet over the page, the work handed in on the left (Canvas's own preview of
    // the file, or the text written), the comments on the right
    function openFeedback(x, from) {
      // (2.99.58) a quiz's feedback is the quiz's own: its answers, marked (screens/quiz.js), never the sheet of what was
      // handed in, which for a quiz holds only Canvas's line about it ("user: …, quiz: …, score: …")
      if (typeList.includes('online_quiz') && a.quiz_id) { app.go(`${c.url}/quizzes/${a.quiz_id}?bcv=feedback`); return; }
      const files = x.attachments || [];
      let at = 0;
      const doc = U.el('bcv-fbs__doc');
      const drawDoc = () => {
        const f = files[at];
        if (f && f.preview_url) doc.replaceChildren(h('iframe', { class: 'bcv-fbs__frame', src: f.preview_url, title: f.display_name || 'Your file', allow: 'fullscreen' }));
        else if (f) doc.replaceChildren(U.el('bcv-fbs__nodoc', [U.text('bcv-fbs__nodoctext', 'This file has no preview here.', 'div'), h('button', { type: 'button', class: 'bcv-btn', text: 'Open the file', onclick: () => BCV.viewer?.open(f, { canvas: true }) })]));
        else if (x.body) doc.replaceChildren(U.el('bcv-fbs__text', [CS().prose(x.body, { cls: 'bcv-asg__prose' })]));
        else doc.replaceChildren(U.el('bcv-fbs__nodoc', [U.text('bcv-fbs__nodoctext', x.submitted_at ? 'Nothing to preview for this hand-in.' : 'Nothing was handed in.', 'div')]));
      };
      drawDoc();
      const tabs = files.length > 1 ? U.el('bcv-fbs__tabs', files.map((f, i) => h('button', { type: 'button', class: `bcv-fbs__tab${i === at ? ' is-on' : ''}`, text: f.display_name || `File ${i + 1}`, onclick: (e) => { at = i; drawDoc(); e.currentTarget.parentNode.querySelectorAll('.bcv-fbs__tab').forEach((t, k) => t.classList.toggle('is-on', k === i)); } }))) : null;
      const list = (x.submission_comments || []).slice().sort((p, q) => String(p.created_at).localeCompare(String(q.created_at)));
      const side = U.el('bcv-fbs__side', [
        U.el('bcv-fbs__sidehead', [h('h3', { class: 'bcv-fbs__sidetitle', text: 'Comments' }), list.length ? U.text('bcv-fbs__count', String(list.length), 'span') : null]),
        list.length ? U.el('bcv-fbs__comments', list.map((cm) => U.el('bcv-fbs__comment', [
          U.el('bcv-fbs__commenthead', [U.text('bcv-fbs__who', cm.author_name || cm.author?.display_name || 'Someone', 'span'), cm.attempt ? U.text('bcv-fbs__attempt', `Attempt ${cm.attempt}`, 'span') : null]),
          cm.created_at ? U.text('bcv-fbs__when', U.fmtAt(cm.created_at), 'div') : null,
          U.text('bcv-fbs__said', String(cm.comment || '').trim(), 'div'),
        ]))) : U.text('bcv-fbs__none', 'No comments yet.', 'div'),
      ]);
      const close = () => { ov.classList.add('is-closing'); document.removeEventListener('keydown', onKey, true); setTimeout(() => ov.remove(), 180); from?.focus?.({ preventScroll: true }); };
      const onKey = (e) => { if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); close(); } };
      const sheet = U.el('bcv-fbs', [
        U.el('bcv-fbs__head', [
          h('span', { class: 'bcv-fbs__tile', 'aria-hidden': 'true' }, U.svg(IC.disc, { size: 16, stroke: 'currentColor', width: 2.1 })),
          U.el('bcv-fbs__titles', [U.text('bcv-fbs__title', 'Feedback', 'div'), U.text('bcv-fbs__sub bcv-ellip', a.name, 'div')]),
          h('button', { type: 'button', class: 'bcv-fbs__close', text: 'Close', onclick: close }),
        ]),
        U.el('bcv-fbs__body', [U.el('bcv-fbs__left', [tabs, doc]), side]),
      ], { role: 'dialog', 'aria-modal': 'true', 'aria-label': `Feedback: ${a.name}` });
      const ov = U.el('bcv-fbs-ov', [sheet], { style: courseVars(c) });
      ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
      document.addEventListener('keydown', onKey, true);
      (document.getElementById('bcv-app') || document.body).append(ov);
      sheet.querySelector('.bcv-fbs__close').focus({ preventScroll: true });
    }
    const feedbackBtn = (x) => (x.submitted_at || (x.submission_comments || []).length || postedOf(x)
      ? h('button', { type: 'button', class: 'bcv-asg__seefb', onclick: (e) => openFeedback(current, e.currentTarget) }, [U.svg(IC.disc, { size: 15, stroke: 'currentColor', width: 2 }), h('span', { text: 'See Feedback' })])
      : null);

    // the right-hand side: the big action, the grade, the details
    const LETTERS = [[97, 'A+'], [93, 'A'], [90, 'A-'], [87, 'B+'], [83, 'B'], [80, 'B-'], [77, 'C+'], [73, 'C'], [70, 'C-'], [67, 'D+'], [60, 'D'], [0, 'F']];
    const letterOf = (x, p) => (a.grading_type === 'letter_grade' && x.grade ? String(x.grade) : p === null ? '' : LETTERS.find(([m]) => p >= m)[1]);
    function gradeRing(x) {
      const p = pct(x), tone = toneOf(p), f = Math.max(0, Math.min(1, (Number(x.score) || 0) / Math.max(1, Number(a.points_possible) || 1)));
      const R = 34, L = 2 * Math.PI * R;
      const ring = h('span', { class: `bcv-asg__ring is-${tone}`, 'aria-hidden': 'true' });
      const NS = 'http://www.w3.org/2000/svg';
      const el = (tag, attrs) => { const e = document.createElementNS(NS, tag); for (const k in attrs) e.setAttribute(k, attrs[k]); return e; };
      const svg = el('svg', { viewBox: '0 0 80 80', width: '80', height: '80' });
      svg.append(el('circle', { cx: '40', cy: '40', r: String(R), class: 'bcv-asg__ringtrack' }), el('circle', { cx: '40', cy: '40', r: String(R), class: 'bcv-asg__ringarc', 'stroke-dasharray': `${(L * f).toFixed(2)} ${L.toFixed(2)}`, transform: 'rotate(-90 40 40)' }));
      ring.append(svg);
      ring.append(h('span', { class: 'bcv-asg__ringletter', text: letterOf(x, p) }));
      return ring;
    }
    function classLine() {
      const st = a.score_statistics;
      if (!st || st.mean === undefined || st.mean === null) return null;
      const bits = [`Class mean ${store.fmtPts(Math.round(st.mean * 10) / 10)}`, st.max !== undefined ? `high ${store.fmtPts(st.max)}` : null, st.min !== undefined ? `low ${store.fmtPts(st.min)}` : null].filter(Boolean);
      return h('div', { class: 'bcv-asg__classline' }, [U.svg(IC.people, { size: 14, stroke: 'currentColor', width: 2 }), h('span', { text: bits.join(' · ') })]);
    }
    // (2.99.53) The Mac's right column: what to do next, as wide as the column — Hand In (or Open Tool, Take Quiz, Open
    // Discussion) lit in the course's colour; handed in and it may be handed in again, what was handed in beside it (See
    // Feedback once it is marked, View Submission till then); with nothing to do here, why, and See Feedback — then the
    // grade, then the facts.
    const TRAY_UP = 'M12 15V4M8 8l4-4 4 4M5 14v4a2 2 0 002 2h10a2 2 0 002-2v-4';
    const DOC_LOOK = 'M14 3H7a2 2 0 00-2 2v14a2 2 0 002 2h4M14 3l5 5M14 3v5h5M16 13a3 3 0 100 6 3 3 0 000-6zM18.2 18.2L21 21';
    const hasWork = (x) => !!(x.submitted_at || postedOf(x) || (x.attachments || []).length || x.body || x.url);
    function whyOf(x) {
      if (closedNow() && handsIn) return `Closed ${U.fmtAt(lockAt)}: nothing can be handed in now.`;
      if (lockedNow() && handsIn) return unlockAt && unlockAt > Date.now() ? `Opens ${macAt(unlockAt)}.` : (a.lock_explanation ? htmlToText(a.lock_explanation, 160) : 'Locked.');
      if (nativeSubmit && !isTool && a.can_submit === false) return 'Submissions are closed.';
      if (nativeSubmit && !isTool && !leftOf(x)) return `No attempts left: this assignment allows ${U.plural(allowed, 'attempt')}.`;
      if (!handsIn) return typeList.includes('on_paper') ? 'Handed in on paper.' : 'Nothing is handed in for this one.';
      return '';
    }
    // (2.99.55) a quiz taken, as the Mac's assignment page has it: Continue Quiz while an attempt is under way; else See
    // Feedback where its results show, and New Attempt beside it while one is left — straight into the attempt, no intro
    // between. Read from the quiz once the page is up (until then: its feedback, and New Attempt while the assignment's
    // own count leaves one).
    const quizKind = typeList.includes('online_quiz') && !!a.quiz_id;
    let quizNow = null;
    const quizTaken = (x) => !!(x.submitted_at || gradedOf(x));
    function quizSteps(x) {
      if (!quizKind || !quizTaken(x)) return null;
      // (2.99.58) an attempt under way: Continue Quiz — the smaller of two, beside See Feedback, while an earlier attempt's
      // feedback can be seen
      if (quizNow?.underWay) return quizNow.feedback
        ? [{ title: 'Continue Quiz', short: 'Continue', icon: IC.bolt, go: 'quizgo' }, { title: 'See Feedback', short: 'Feedback', icon: IC.disc, go: 'quizfb' }]
        : [{ title: 'Continue Quiz', icon: IC.bolt, go: 'quizgo' }];
      const out = [];
      if (quizNow ? quizNow.feedback : true) out.push({ title: 'See Feedback', short: 'Feedback', icon: IC.disc, go: 'quizfb' });
      if (quizNow ? quizNow.canStart : leftOf(x)) out.push({ title: 'New Attempt', short: 'Retake', icon: IC.bolt, go: 'quizgo' });
      return out.length ? out : null;
    }
    function stepsOf(x) {
      const quizzed = quizSteps(x);
      if (quizzed) return quizzed;
      const one = (() => {
        if (canAgain(x)) return x.submitted_at || used(x) > 0 ? { title: 'Hand In Again', short: 'Hand In', icon: TRAY_UP, go: 'hand' } : { title: 'Hand In', icon: TRAY_UP, go: 'hand' };
        if (isTool && !lockedNow()) return { title: 'Open Tool', icon: IC.tool, go: 'tool' };
        if (typeList.includes('online_quiz') && a.quiz_id) return { title: 'Take Quiz', icon: IC.bolt, go: 'quiz' };
        if (typeList.includes('discussion_topic') && a.discussion_topic?.id) return { title: 'Open Discussion', icon: IC.disc, go: 'disc' };
        if (canvasOnly && !closedNow() && !lockedNow()) return { title: `Hand In on ${BCV.lms.name}`, short: 'Hand In', icon: 'M9 6h9v9M18 6L7 17', go: 'canvas' };
        return null;
      })();
      if (one && (one.go === 'hand' || one.go === 'canvas') && hasWork(x)) {
        const marked = postedOf(x) || (x.submission_comments || []).length;
        return [marked ? { title: 'See Feedback', short: 'Feedback', icon: IC.disc, go: 'look' } : { title: 'View Submission', short: 'Submission', icon: DOC_LOOK, go: 'look' }, one];
      }
      return one ? [one] : [];
    }
    const doStep = (step, el) => {
      if (step.go === 'hand') openHand(el);
      else if (step.go === 'look') openFeedback(current, el);
      else if (step.go === 'tool') launchTool(el);
      else if (step.go === 'quiz') app.go(`${c.url}/quizzes/${a.quiz_id}`);
      else if (step.go === 'quizgo') app.go(`${c.url}/quizzes/${a.quiz_id}?bcv=take&begin=1`);
      else if (step.go === 'quizfb') app.go(`${c.url}/quizzes/${a.quiz_id}?bcv=feedback`);
      else if (step.go === 'disc') app.go(`${c.url}/discussion_topics/${a.discussion_topic.id}`);
      else if (step.go === 'canvas') app.go(nativeHref(`${c.url}/assignments/${a.id}`));
    };
    function stepsEl(x) {
      const steps = stepsOf(x);
      const why = whyOf(x);
      if (!steps.length) {
        if (!why && !hasWork(x)) return null;
        return U.el('bcv-asg__panel bcv-asg__whycard', [
          why ? h('div', { class: 'bcv-asg__why' }, [U.svg('M12 3a9 9 0 100 18 9 9 0 000-18zM12 11v5M12 8h.01', { size: 15, stroke: 'currentColor', width: 2 }), h('span', { text: why })]) : null,
          hasWork(x) ? h('button', { type: 'button', class: 'bcv-asg__step bcv-asg__seefb bcv-asg__pill is-done', title: 'Your work, with your teacher\'s marks and comments', 'aria-haspopup': 'dialog', onclick: (e) => openFeedback(current, e.currentTarget) }, [U.svg(IC.disc, { size: 16, stroke: 'currentColor', width: 2 }), h('span', { 'data-glide': 'word', text: 'See Feedback' })]) : null,
        ].filter(Boolean));
      }
      const two = steps.length > 1;
      const btns = steps.map((st, i) => {
        const lit = i === steps.length - 1;
        const legacy = st.go === 'look' ? 'bcv-asg__pill is-done' : st.go === 'tool' ? 'bcv-asg__pill is-go bcv-detail__tool' : 'bcv-asg__pill is-go';
        return h('button', { type: 'button', class: `bcv-asg__step ${lit ? 'is-lit' : ''} ${legacy}`, title: st.title, 'aria-haspopup': st.go === 'hand' || st.go === 'look' ? 'dialog' : null, 'aria-expanded': st.go === 'hand' ? 'false' : null, onclick: (e) => doStep(st, e.currentTarget) }, [
          U.svg(st.icon, { size: 17, stroke: 'currentColor', width: 2.1 }), h('span', { 'data-glide': 'word', text: two && st.short ? st.short : st.title }),
        ]);
      });
      const drops = steps.some((st) => st.go === 'hand') && (typeList.includes('online_upload') || typeList.includes('online_url'));
      return U.el('bcv-asg__steps', [
        U.el('bcv-asg__stepbtns', btns),
        drops ? U.text('bcv-asg__drophint', typeList.includes('online_upload') ? 'Or drop files on this page.' : 'Or drop a link on this page.', 'div') : null,
        why ? h('div', { class: 'bcv-asg__why' }, [U.svg('M12 3a9 9 0 100 18 9 9 0 000-18zM12 11v5M12 8h.01', { size: 15, stroke: 'currentColor', width: 2 }), h('span', { text: why })]) : null,
      ].filter(Boolean));
    }
    function railEl(x) {
      const parts = [stepsEl(x)];
      if (postedOf(x)) parts.push(U.el('bcv-asg__panel bcv-asg__gradep', [h('div', { class: 'bcv-asg__panelk', text: 'Grade' }), U.el('bcv-asg__graderow', [gradeRing(x), actionEl(x)]), classLine(), a.rubric?.length ? rubricFact() : null].filter(Boolean)));
      else if (heldOf(x)) parts.push(U.el('bcv-asg__panel bcv-asg__held', [h('div', { class: 'bcv-asg__why' }, [U.svg('M3 3l18 18M10.6 5.1A10 10 0 0122 12s-1 2.2-3.2 4.2M6.4 6.4C3.8 8.1 2 12 2 12s3.5 7 10 7c1.8 0 3.4-.5 4.8-1.3', { size: 15, stroke: 'currentColor', width: 2 }), h('span', { text: 'Graded, but your teacher has not released the grade yet.' })])]));
      // (a module's Mark as done, and a quiz's Open quiz, kept under the steps till there is a grade card to hold them)
      if (!postedOf(x)) {
        const done = slot('bcv-detail__doneslot');
        fill(done, ({ modItem }) => doneButton(ctx, c, a, modItem, { primary: !handsIn }));
        parts.push(U.el('bcv-asg__action bcv-asg__extras', [a.quiz_id && !typeList.includes('online_quiz') ? U.btn('Open quiz', { icon: IC.bolt, onClick: () => app.go(`${c.url}/quizzes/${a.quiz_id}`) }) : null, done].filter(Boolean)));
      }
      parts.push(factsEl(x));
      return U.el('bcv-asg__rail', parts.filter(Boolean));
    }
    // (2.99.53) files dropped on the page, as on the Mac's: the hand-in opens with them in
    const takeDrop = (files) => {
      const pill = page.querySelector('.bcv-asg__step.is-go:not(.bcv-detail__tool)');
      if (!pill || !files?.length) return;
      openHand(pill);
      const put = (n = 0) => {
        let input = handScreen?.querySelector('input[type=file]');
        if (!input) { const tab = handScreen?.querySelector('.bcv-sb__tab[data-tab="file"]'); if (tab && !tab.classList.contains('is-active')) tab.click(); input = handScreen?.querySelector('input[type=file]'); }
        if (!input) { if (n < 20) setTimeout(() => put(n + 1), 60); return; }
        try { const dt = new DataTransfer(); for (const f of files) dt.items.add(f); input.files = dt.files; input.dispatchEvent(new Event('change', { bubbles: true })); } catch { /* the panel is open: they can be chosen there */ }
      };
      setTimeout(put, 80);
    };
    let railNow = railEl(s);
    const left = U.el('bcv-asg__left', [workSection, comments, instructions]); // (2.99.51: the work and what was said, then what was asked)
    const colsEl = U.el('bcv-asg__cols', [railNow, left]); // (the grade read before the instructions; drawn on the right when wide)
    const page = U.el('bcv-asg', [top, headEl, colsEl].filter(Boolean), { style: { '--bcv-cc': c.color || 'var(--bcv-blue)', '--bcv-cc-ink': c.palette?.text || c.color || 'var(--bcv-blue)' } });
    /** Drawn again from a fresher submission (a tool's grade landing): the status, the action, the grade and the facts, in place. */
    const repaint = (x) => {
      current = x;
      const st2 = statusEl(x), r2 = railEl(x);
      statusNow.replaceWith(st2); railNow.replaceWith(r2);
      statusNow = st2; railNow = r2;
      drawComments(x);
      drawWork(x);
    };
    if (quizKind) {
      Promise.all([store.quiz(c.id, a.quiz_id), store.quizSubmissions(c.id, a.quiz_id)]).then(([q, subs]) => {
        if (!q || !ctx.alive()) return;
        const limit = store.quizAttemptLimit(q, subs || []);
        const underWay = (subs || []).some((x) => x.workflow_state === 'untaken');
        const finishedN = (subs || []).filter((x) => x.workflow_state === 'complete' || x.workflow_state === 'pending_review').length;
        const survey = /survey/.test(q.quiz_type || '');
        const held = current.posted_at === null && !!(current.submitted_at || gradedOf(current));
        const hidden = q.hide_results === 'always' || (q.hide_results === 'until_after_last_attempt' && limit.allowed !== null && limit.left > 0) || held;
        quizNow = { underWay, canStart: !q.locked_for_user && !q.require_lockdown_browser && (limit.left === null || limit.left > 0 || underWay), feedback: (finishedN > 0 || quizTaken(current)) && !survey && !hidden };
        repaint(current);
      }).catch(() => {});
    }
    page.addEventListener('dragover', (e) => { if (canAgain(current) && typeList.includes('online_upload') && [...(e.dataTransfer?.types || [])].includes('Files')) { e.preventDefault(); page.classList.add('is-dropping'); } });
    page.addEventListener('dragleave', (e) => { if (!page.contains(e.relatedTarget)) page.classList.remove('is-dropping'); });
    page.addEventListener('drop', (e) => { page.classList.remove('is-dropping'); if (e.target.closest?.('.bcv-sb__drop')) return; if (!canAgain(current) || !e.dataTransfer?.files?.length) return; e.preventDefault(); takeDrop([...e.dataTransfer.files]); });
    main.replaceChildren(U.card(page, 'bcv-card--22 bcv-asg__card'));
    // opened to hand in (a To Do row's Submit, ?bcv=submit): the panel grows out of the pill once the page is up
    if (handScreen && route.params.get('bcv') === 'submit' && canAgain(s)) {
      const go = (n = 0) => { const pill = page.querySelector('.bcv-asg__pill.is-go, .bcv-asg__again'); if (pill?.isConnected && pill.getBoundingClientRect().width) { if (pill.classList.contains('bcv-asg__again')) openHand(page.querySelector('.bcv-asg__pill')); else openHand(pill); } else if (n < 40 && ctx.alive()) setTimeout(() => go(n + 1), 50); };
      setTimeout(go, 120);
    }
    // A tool's grade lands behind the page's back: the tool passes it back after its launch (some
    // only when the student opens the assignment), and a page drawn a moment earlier would keep
    // showing nothing. So the submission is asked for again once the tool has loaded and then for a
    // while — every few seconds at first, then every quarter minute for three minutes, while the
    // tab is looked at — and the pill, the status and the facts are drawn again when it changes, in
    // place, without touching the tool's frame.
    if (isTool) {
      let shown = s, tries = 0, timer = 0, started = false;
      const changed = (x) => !!x && (x.score !== shown.score || x.workflow_state !== shown.workflow_state || x.posted_at !== shown.posted_at || x.grade !== shown.grade || x.submitted_at !== shown.submitted_at || x.attempt !== shown.attempt);
      const poll = async () => {
        timer = 0;
        if (!ctx.alive()) return;
        if (document.visibilityState === 'visible') {
          const fresh = await store.submission(c.id, route.arg, { force: true }).catch(() => null);
          if (!ctx.alive()) return;
          if (changed(fresh)) {
            shown = fresh;
            repaint(fresh);
            store.invalidateGrades().catch(() => {}); // every other screen with a score asks again
          }
          tries++;
        }
        if (tries < 14) timer = setTimeout(poll, tries < 3 ? 4000 : 15000);
      };
      const start = () => { if (started) return; started = true; timer = setTimeout(poll, 2500); };
      startPoll = start;
      setTimeout(start, 8000); // a tool never opened still gets its looks: a grade can land from an earlier sitting
      ctx.onLeave?.(() => clearTimeout(timer));
    }
    side.remove(); // (one column: the page is the mockup's, the facts in its own boxes)
    return b;
  };

  D.discussion = async (ctx, shell, { announcement = false }) => {
    const { app, route } = ctx;
    const c = shell.course;
    const b = cols();
    const main = mainCol(), side = sideCol();
    b.append(main, side);
    main.append(U.loading());
    const K = { kind: shell.kind };
    const [t, view] = await Promise.all([store.discussion(c.id, route.arg, K).catch(() => null), store.discussionView(c.id, route.arg, K).catch(() => null)]);
    if (!ctx.alive()) return b;
    if (!t) return main.replaceChildren(U.errorBox('This discussion could not be loaded.')) || b;
    app.nameHere?.(t.title); // the next screen's Back names this thread
    store.markTopicRead(c.id, t.id, K);
    shell.reader = { title: t.title, html: t.message || '' };
    const people = new Map((view?.participants || []).map((p) => [String(p.id), p]));
    let replyTo = null;
    const replyBox = h('textarea', { class: 'bcv-textarea', placeholder: announcement ? 'Comment on this announcement…' : 'Write your reply…', rows: 4 });
    const replyTitle = U.text('bcv-reply__title', 'Reply');
    // a reply aimed at one post can be aimed at the thread again (the button shows while one is)
    const cancelReply = h('button', { type: 'button', class: 'bcv-btn', text: 'Cancel reply-to', hidden: true, onclick: () => { replyTo = null; replyTitle.textContent = 'Reply'; cancelReply.hidden = true; replyBox.focus(); } });
    const post = U.btn('Post reply', { kind: 'primary', icon: IC.send, iconColor: '#fff', onClick: async () => {
      const text = replyBox.value.trim();
      if (!text) return;
      post.disabled = true;
      try {
        await store.postEntry(c.id, t.id, text.split(/\n{2,}/).map((p) => `<p>${escapeHtml(p).replace(/\n/g, '<br>')}</p>`).join(''), replyTo?.id || null, K);
        replyBox.value = '';
        replyTo = null;
        cancelReply.hidden = true;
        U.toast('Reply posted');
        app.render();
      } catch (e) {
        U.toast(`Could not post: ${e.message}`, { error: true });
      } finally {
        post.disabled = false;
      }
    } });
    const entryEl = (e, depth) => {
      const author = people.get(String(e.user_id));
      // (a reply's own files: the view gives one as `attachment`, newer Canvas as `attachments` — a reply may be a file alone)
      const seen = new Set();
      const files = e.deleted ? [] : [...(Array.isArray(e.attachments) ? e.attachments : []), ...(e.attachment ? [e.attachment] : [])].filter((x) => x?.url && !seen.has(x.url) && seen.add(x.url));
      return U.el(`bcv-entry ${depth === 1 ? 'bcv-entry--reply' : depth >= 2 ? 'bcv-entry--reply2' : ''}`, [
        U.avatar(author?.avatar_image_url, author?.display_name, 38),
        U.el('bcv-entry__body', [
          U.el('bcv-entry__head', [U.text('bcv-entry__author', author?.display_name || 'Deleted user', 'span'), U.text('bcv-entry__date', U.fmtAtUpper(e.created_at), 'span'), e.deleted ? U.badge('Deleted', '', 'bcv-badge--xs') : null]),
          e.deleted || (!e.message && files.length) ? null : CS().prose(e.message || '', { cls: 'bcv-prose--14 bcv-entry__msg' }),
          files.length ? U.el('bcv-chips', files.map((att) => h('a', { class: 'bcv-chip', href: att.url, target: '_blank', rel: 'noopener', text: att.display_name || att.filename || 'Attachment' }))) : null,
          t.locked || e.deleted ? null : U.el('bcv-entry__actions', h('button', { type: 'button', class: 'bcv-entry__action', text: 'Reply', onclick: () => { replyTo = { id: e.id, name: author?.display_name || 'this post' }; replyTitle.textContent = `Reply to ${replyTo.name}`; cancelReply.hidden = false; replyBox.focus(); } })),
        ]),
      ]);
    };
    const flatten = (entries, depth = 0) => entries.flatMap((e) => [entryEl(e, depth), ...flatten(e.replies || [], depth + 1)]);
    const entries = view ? flatten(view.view || []) : [];
    main.replaceChildren(
      itemTop(ctx, c, backTo(app, announcement ? `${c.url}/announcements` : `${c.url}/discussion_topics`, announcement ? 'Announcements' : 'Discussions'), announcement ? 'Announcement' : 'Discussion', t.id, { kind: shell.kind }),
      U.card([
        U.el('bcv-detail', [
          h('h2', { class: 'bcv-detail__title bcv-pretty', text: t.title }),
          U.el('bcv-row__head', [
            U.avatar(t.author?.avatar_image_url, t.author?.display_name, 38),
            h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-entry__author', t.author?.display_name || t.user_name || (announcement ? 'Announcement' : 'Discussion')), U.text('bcv-entry__date', [
              U.fmtAtUpper(t.posted_at || t.delayed_post_at),
              pts(t.assignment?.points_possible), // a graded discussion Canvas gave no points for says nothing, not "null pts"
              t.assignment?.due_at ? `due ${U.fmtAtUpper(t.assignment.due_at)}` : t.todo_date ? `to do ${U.fmtAtUpper(t.todo_date)}` : null,
              t.lock_at ? (U.parse(t.lock_at) > new Date() ? `open until ${U.fmtAtUpper(t.lock_at)}` : `closed ${U.fmtAtUpper(t.lock_at)}`) : null, // (a window that has passed is closed, not "available until")
            ].filter(Boolean).join(' · '))]),
            t.locked ? U.badge('Closed for comments') : null,
          ]),
          CS().prose(t.message || ''),
          (t.attachments || []).length ? U.el('bcv-chips', t.attachments.map((att) => h('a', { class: 'bcv-chip', href: att.url, target: '_blank', rel: 'noopener', text: att.display_name }))) : null,
        ]),
        entries.length ? h('div', {}, entries) : (view ? U.empty(announcement ? 'No comments yet.' : 'No replies yet.') : null),
        t.locked || (announcement && t.locked) ? null : U.el('bcv-reply', [replyTitle, replyBox, h('div', { style: { display: 'flex', justifyContent: 'flex-end', gap: '8px' } }, [cancelReply, post])]),
      ], 'bcv-card--22 bcv-card--list'),
    );
    side.append(h('div', {}, [U.label('About this thread'), U.card(U.el('bcv-detail', [
      meta([['Replies', t.discussion_subentry_count ?? entries.length], ['Unread', t.unread_count ?? 0], ['Last post', t.last_reply_at ? U.fmtAtUpper(t.last_reply_at) : null], ['Type', announcement ? 'Announcement' : t.assignment ? 'Graded discussion' : 'Discussion'], ['Points', t.assignment?.points_possible ?? null], ['Due', t.assignment?.due_at ? U.fmtAt(t.assignment.due_at) : null]]),
      t.require_initial_post ? U.text('bcv-hint', 'You must post before you can see other replies.') : null,
    ]), 'bcv-card--22')]));
    return b;
  };

  D.page = async (ctx, shell) => {
    const { app, route } = ctx;
    const c = shell.course;
    const b = cols();
    const main = mainCol(), side = sideCol();
    b.append(main, side);
    main.append(U.loading());
    const p = await store.page(c.id, route.arg, { kind: shell.kind }).catch(() => null);
    if (!ctx.alive()) return b;
    if (!p) return main.replaceChildren(U.errorBox('This page could not be loaded.')) || b;
    shell.reader = { title: p.title, html: p.body || '' };
    app.nameHere?.(p.title); // the next screen's Back names this page
    // Mark as done, at the foot of the page, where a module asks for it (a page of lecture videos,
    // a reading): asked for after the page is drawn, so the page never waits on the modules
    const slug = p.url || route.arg;
    const doneSlot = h('div', { class: 'bcv-detail__actions bcv-detail__actions--foot', hidden: true });
    main.replaceChildren(
      itemTop(ctx, c, backTo(app, `${c.url}/pages`, 'Pages'), 'Page', p.url || route.arg, { kind: shell.kind }),
      U.card(U.el('bcv-detail', [
        h('div', { style: { display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' } }, [h('h2', { class: 'bcv-detail__title bcv-pretty', text: p.title }), p.front_page ? U.badge('Front page', 'green', 'bcv-badge--sm') : null]),
        U.text('bcv-entry__date', [
          p.created_at ? `Created ${U.fmtDateComma(p.created_at)}` : null, // a page Canvas gives no dates for says nothing, not a bare "·"
          p.updated_at ? `last edited ${U.fmtDateComma(p.updated_at)}${p.last_edited_by?.display_name ? ` by ${p.last_edited_by.display_name}` : ''}` : null,
        ].filter(Boolean).join(' · ')),
        CS().prose(p.body || ''),
        doneSlot,
      ]), 'bcv-card--22'),
    );
    store.moduleItemFor(c.id, 'Page', slug, { itemId: route.params?.get?.('module_item_id') || null }).catch(() => null).then((item) => {
      if (!ctx.alive()) return;
      const btn = doneButton(ctx, c, { id: slug }, item, { primary: true, type: 'Page', noun: 'page' });
      if (!btn) return;
      doneSlot.append(btn);
      doneSlot.hidden = false;
    });
    const links = CS().linksFrom(p.body, 8);
    if (links.length) side.append(h('div', {}, [U.label('Links on this page'), U.card(links.map((l) => U.row([U.svg(IC.link, { size: 15, stroke: 'var(--bcv-blue)', width: 1.8, style: { flex: 'none' } }), U.text('bcv-course-link bcv-ellip', l.text, 'span'), U.chev()], { mod: 'bcv-row--p13-16', href: l.href })), 'bcv-card--list')])); // (append(null) would write the word "null" on the page)
    return b;
  };

  D.syllabus = async (ctx, shell) => {
    const { app } = ctx;
    const c = shell.course;
    const b = cols();
    const main = mainCol(), side = sideCol();
    b.append(main, side);
    main.append(U.loading());
    const [html, list] = await Promise.all([store.syllabus(c.id).catch(() => ''), store.assignments(c.id).catch(() => [])]);
    if (!ctx.alive()) return b;
    shell.reader = { title: 'Syllabus', html };
    main.replaceChildren(
      backTo(app, `${c.url}/assignments`, 'Assignments'),
      U.card(U.el('bcv-detail', [h('h2', { class: 'bcv-detail__title', text: 'Syllabus' }), html ? CS().prose(html) : U.text('bcv-hint', 'No syllabus description has been added.')]), 'bcv-card--22'),
    );
    const dated = (list || []).filter((a) => a.due_at).sort((x, y) => U.parse(x.due_at) - U.parse(y.due_at));
    side.append(h('div', {}, [U.label('Course summary'), dated.length ? U.card(dated.slice(0, 40).map((a) => U.row([
      U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--14 bcv-ellip', a.name), U.text('bcv-row__sub bcv-row__sub--115', `${hasGrade(a) ? 'Graded' : `Due ${U.fmtAt(a.due_at)}`} · ${a.points_possible !== null && a.points_possible !== undefined ? `${store.fmtPts(a.points_possible)} pts` : 'no points'}`)]),
      U.chev(),
    ], { mod: 'bcv-row--p12-16', href: `${c.url}/assignments/${a.id}` })), 'bcv-card--list') : U.emptyCard('No dated assignments.')]));
    return b;
  };

  D.openMark = openMark;
  D.attemptsFact = attemptsFact; // the phone draws the same five facts, from the same fields
  D.doneButton = doneButton;
  D.navRow = navRow;

  // (2.99.55) the row along an item's top — the way back, Previous / Next — for the quiz's intro too (screens/quiz.js)
  D.itemTop = (ctx, c, type, assetId, backHref, backLabel) => itemTop(ctx, c, backTo(ctx.app, backHref, backLabel), type, assetId);
  BCV.screens.courseDetail = D;
})();
