/* Item views inside a course: assignment, discussion thread / announcement,
 * page, quiz and syllabus. The mockup has no screens for these, so they
 * are interpreted in the same language: one reading card, one side column. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h, htmlToText, escapeHtml } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  /** How a quiz is restricted, in words: an access code, an IP filter, Respondus LockDown Browser. */
  const restrictions = (q) => [q.has_access_code || q.access_code ? 'Access code' : null, q.ip_filter ? 'Allowed networks only' : null, q.require_lockdown_browser ? 'LockDown Browser' : null].filter(Boolean);
  const hasGrade = (a) => { const s = a?.submission || {}; return s.workflow_state === 'graded' && s.score !== null && s.score !== undefined && s.posted_at !== null; }; // (marked and posted: a held mark is not yours to see yet)
  const store = BCV.store;
  const CS = () => BCV.screens.course;

  const cols = () => U.el('bcv-body bcv-body--course-cols');
  const mainCol = () => h('div', { class: 'bcv-col', style: { flex: '1 1 560px' } });
  const sideCol = () => h('div', { class: 'bcv-col bcv-col--16', style: { flex: '1 1 300px' } });
  const meta = (pairs) => U.el('bcv-detail__meta', pairs.filter(([, v]) => v !== null && v !== undefined && v !== '').map(([k, v]) => h('span', { class: 'bcv-detail__meta-item' }, [h('b', { text: `${k} ` }), String(v)])));
  const backBtn = (app, href, lbl) => h('button', { type: 'button', class: 'bcv-linkbtn bcv-detail__back', onclick: () => { app.markBack?.(); app.go(href); } }, [U.svg(IC.back, { size: 14, stroke: 'var(--bcv-blue)', width: 2.1 }), lbl]);
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
  /** (2.98.64) The chip opens its box on a hover too, where there is a mouse (U.hoverOpens: after a
   *  moment's rest on it, not again under a pointer that has not moved since the box folded back into
   *  it). A press still opens at once. */
  const hoverOpens = U.hoverOpens;
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
        U.toast(`Canvas did not take the mark: ${e?.message || e}`, { error: true });
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
      h('span', { class: 'bcv-ellip', text: x.name }),
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
    store.itemNeighbours(c.id, type, assetId, { viaModules, itemId, kind, courseUrl: c.url })
      .then((nav) => { if (!ctx.alive() || !slot.parentNode) return; const el = navPill(app, nav); if (el) slot.replaceWith(el); else slot.remove(); })
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
    const statusEl = (x) => { const st = standingOf(x); return h('div', { class: `bcv-asg__status is-${st.tone}` }, [U.svg(st.icon, { size: 14, stroke: 'currentColor', width: 2.2 }), U.text('bcv-asg__statustext', st.text, 'span')]); };

    const GRADING = { points: 'Graded', pass_fail: 'Complete or incomplete', letter_grade: 'Letter grade', gpa_scale: 'GPA scale', percent: 'Percentage', not_graded: 'Not graded' };
    const fact = (k, v, sub, extra = {}) => h(extra.onclick ? 'button' : 'div', { class: `bcv-asg__fact ${extra.cls || ''}`, type: extra.onclick ? 'button' : null, onclick: extra.onclick || null, title: extra.title || null }, [
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
    const factsEl = (x) => U.el(`bcv-asg__facts ${a.rubric?.length ? 'is-four' : ''}`, [
      fact('Due', due ? shortAt(due) : 'No due date', dueSub),
      fact('Points', a.points_possible !== null && a.points_possible !== undefined ? store.fmtPts(a.points_possible) : '—', a.omit_from_final_grade ? 'Doesn’t count toward your grade' : (GRADING[a.grading_type] || 'Graded')),
      fact('Submit as', howWord, handsIn && !typeList.some((t) => t === 'on_paper' || t === 'none') ? attemptsWord(x) : null),
      rubricFact(),
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
    const openCard = (pill, { hover = false } = {}) => {
      const run = () => { opened = openMarkBox(ctx, c, a, current, pill, { anchor: 'center', within: pill.closest('.bcv-asg__card'), hover, onClosed: afterClose, again: canAgain(current) ? (bx) => openHand(pill, bx) : null }); };
      const need = [BCV.screens.feedback?.card ? null : BCV.lazy?.load?.('submit'), a.rubric?.length && !BCV.rubricRing ? BCV.lazy?.load?.('rubric')?.catch(() => null) : null].filter(Boolean);
      if (!need.length) run(); else Promise.all(need).then(() => { if (pill.isConnected) run(); }).catch(() => { if (!hover) app.go(`${c.url}/assignments/${a.id}?bcv=feedback`); });
    };
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
        main = pill('graded', '', null, { cls: `is-${toneOf(p)}`, title: 'Your grade: feedback, attempts and comments', onClick: openCard, kids: [
          U.svg(ICONS.tick, { size: 15, stroke: 'currentColor', width: 2.5 }),
          h('span', { class: 'bcv-asg__score', 'data-glide': 'score', text: store.fmtPts(x.score) }),
          h('span', { class: 'bcv-asg__of', text: `/ ${a.points_possible ?? '—'}` }),
          letter || p !== null ? h('span', { class: 'bcv-asg__pct', text: letter || `${p}%` }) : null,
        ] });
        aside = note(x.late ? `Late${x.points_deducted ? ` · −${store.fmtPts(x.points_deducted)} pts` : ''}` : null, comments ? U.plural(comments, 'comment') : null, classPlot(a.score_statistics, x.score, a.points_possible, toneOf(p)));
      } else if (heldOf(x) || x.submitted_at || x.excused) {
        const word = x.excused ? 'Excused' : heldOf(x) ? 'Submitted' : x.late ? 'Submitted late' : 'Submitted';
        main = pill('done', word, ICONS.tick, { cls: x.excused ? 'is-grey' : '', title: 'What you handed in, and comments', onClick: x.submitted_at || heldOf(x) ? openCard : null, off: !(x.submitted_at || heldOf(x)) });
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
      // the mark (or what was handed in) opens on a moment's rest of the pointer too, as the chip it replaced did (U.hoverOpens)
      if (main && (main.classList.contains('is-graded') || (main.classList.contains('is-done') && !main.disabled))) hoverOpens(main, (el) => openCard(el, { hover: true }));
      const row = U.el('bcv-asg__action', [main, aside, ...extras]);
      return row;
    }

    // ---- the row along the top: the way back, and the assignments either side --------------------------------------
    const backEl = h('button', { type: 'button', class: 'bcv-linkbtn bcv-detail__back bcv-asg__back', onclick: () => { app.markBack?.(); app.go(back.href); } }, [U.svg('M15 5l-7 7 7 7', { size: 15, stroke: 'currentColor', width: 2.4 }), back.label]);
    const top = itemTop(ctx, c, backEl, 'Assignment', a.id);
    const titleEl = h('h1', { class: 'bcv-detail__title bcv-asg__title bcv-pretty', text: a.name });
    let statusNow = statusEl(s), factsNow = factsEl(s), actionNow = actionEl(s);
    const headEl = U.el('bcv-detail__head bcv-asg__head', [titleEl, statusNow]);
    const desc = a.description && htmlToText(a.description, 40).trim() ? CS().prose(a.description, { cls: 'bcv-asg__prose' }) : (isTool ? null : U.text('bcv-asg__quiet', 'No instructions were given.'));
    const page = U.el('bcv-asg', [top, headEl, factsNow, actionNow, desc].filter(Boolean), { style: { '--bcv-cc': c.color || 'var(--bcv-blue)', '--bcv-cc-ink': c.palette?.text || c.color || 'var(--bcv-blue)' } });
    /** Drawn again from a fresher submission (a tool's grade landing): the pill, the status and the facts, in place. */
    const repaint = (x) => {
      current = x;
      const st2 = statusEl(x), f2 = factsEl(x), a2 = actionEl(x);
      statusNow.replaceWith(st2); factsNow.replaceWith(f2); actionNow.replaceWith(a2);
      statusNow = st2; factsNow = f2; actionNow = a2;
    };
    main.replaceChildren(U.card(page, 'bcv-card--22 bcv-asg__card')); // (the page's one card holds everything: the way back to the instructions)
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
      return U.el(`bcv-entry ${depth === 1 ? 'bcv-entry--reply' : depth >= 2 ? 'bcv-entry--reply2' : ''}`, [
        U.avatar(author?.avatar_image_url, author?.display_name, 38),
        U.el('bcv-entry__body', [
          U.el('bcv-entry__head', [U.text('bcv-entry__author', author?.display_name || 'Deleted user', 'span'), U.text('bcv-entry__date', U.fmtAtUpper(e.created_at), 'span'), e.deleted ? U.badge('Deleted', '', 'bcv-badge--xs') : null]),
          e.deleted ? null : CS().prose(e.message || '', { cls: 'bcv-prose--14 bcv-entry__msg' }),
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

  D.quiz = async (ctx, shell) => {
    const { app, route } = ctx;
    const c = shell.course;
    const b = cols();
    const main = mainCol(), side = sideCol();
    b.append(main, side);
    main.append(U.loading());
    const [q, subs] = await Promise.all([store.quiz(c.id, route.arg).catch(() => null), store.quizSubmissions(c.id, route.arg).catch(() => [])]);
    // Canvas keeps one quiz submission per student — the one in play — so the attempts before it are
    // not in that list at all. Every finished attempt is in the assignment submission's history.
    const asub = q?.assignment_id ? await store.submission(c.id, q.assignment_id).catch(() => null) : null;
    // a grade not posted yet (Canvas: "Your quiz has been muted") keeps the results back: no score, no feedback (quiz.js heldBack)
    const held = !!asub && asub.posted_at === null && !!(asub.submitted_at || asub.workflow_state === 'graded' || asub.workflow_state === 'pending_review');
    if (!ctx.alive()) return b;
    if (!q) return main.replaceChildren(U.errorBox('This quiz could not be loaded.')) || b;
    shell.reader = { title: q.title, html: q.description || '' };
    app.nameHere?.(q.title);
    const TYPE = { assignment: 'Graded quiz', practice_quiz: 'Practice quiz', graded_survey: 'Graded survey', survey: 'Survey' };
    // attempts used/allowed from Canvas's own count on the submission; the Take button goes away at the limit
    const limit = store.quizAttemptLimit(q, subs);
    const open = (subs || []).some((s) => s.workflow_state === 'untaken');
    const noneLeft = limit.allowed !== null && limit.left <= 0 && !open;
    const finished = (subs || []).filter((s) => s.workflow_state === 'complete' || s.workflow_state === 'pending_review');
    const latest = finished.slice().sort((a, b) => (Number(b.attempt) || 0) - (Number(a.attempt) || 0))[0] || null;
    const feedbackHref = (s) => `${c.url}/quizzes/${q.id}?bcv=feedback&sub=${encodeURIComponent(s.id)}`;
    // an earlier attempt has no quiz submission of its own to name, so it is asked for by its number
    const feedbackAt = (n) => `${c.url}/quizzes/${q.id}?bcv=feedback&attempt=${n}`;
    const takeHref = `${c.url}/quizzes/${q.id}?bcv=take`; // every quiz is taken here, one question at a time included
    /** Every attempt: the finished ones from the history, and the one in play from the live submission. */
    function attemptList() {
      const hist = (asub?.submission_history || []).filter((x) => Number(x.attempt) > 0);
      const done = hist.length
        ? hist.map((x) => ({ n: Number(x.attempt), score: x.score, at: x.submitted_at || x.graded_at, href: feedbackAt(Number(x.attempt)), live: false }))
        : finished.map((s) => ({ n: Number(s.attempt) || 0, score: s.score, at: s.finished_at, href: feedbackHref(s), live: false }));
      const inPlay = (subs || []).find((s) => s.workflow_state === 'untaken');
      // the open attempt carries the best score so far, which is an earlier attempt's: it shows none
      if (inPlay) done.push({ n: Number(inPlay.attempt) || done.length + 1, score: null, at: null, href: takeHref, live: true });
      return done.sort((x, y) => x.n - y.n).filter((x, i, all) => i === all.findIndex((z) => z.n === x.n));
    }
    const attempts = attemptList();
    const attemptRows = attempts.map((x) => U.row([
      U.tile(IC.bolt, { color: '#7d7bef', tint: 'rgba(88,86,214,.16)' }),
      U.el('bcv-row__body', [U.text('bcv-row__title bcv-row__title--145', `Attempt ${x.n}`), U.text('bcv-row__sub', x.live ? 'In progress' : (x.at ? `Finished ${U.fmtAt(x.at)}` : 'Finished'))]),
      U.badge(!x.live && !held && x.score !== null && x.score !== undefined ? `${store.fmtPts(x.score)} / ${q.points_possible}` : '—', x.live || held ? '' : 'green'),
      // a finished attempt opens its own feedback; the one in play resumes
    ], { mod: 'bcv-row--p12', href: x.href }));
    /** What the header says about attempts, with the one being taken counted as taken. */
    function attemptsLine() {
      const inPlay = attempts.find((x) => x.live);
      const used = attempts.length; // the open one is one of them
      if (limit.allowed === null) return `${used} used · unlimited`;
      return inPlay ? `${used} of ${limit.allowed} used · attempt ${inPlay.n} in progress` : `${used} of ${limit.allowed} used`;
    }
    main.replaceChildren(
      itemTop(ctx, c, backTo(app, `${c.url}/quizzes`, 'Quizzes'), 'Quiz', q.id),
      U.card(U.el('bcv-detail', [
        h('h2', { class: 'bcv-detail__title bcv-pretty', text: q.title }),
        meta([['Due', q.due_at ? U.fmtAt(q.due_at) : 'No due date'], ['Points', q.points_possible ?? '—'], ['Questions', q.question_count ?? '—'], ['Time limit', q.time_limit ? `${q.time_limit} minutes` : 'None'], ['Attempts', attemptsLine()], ['Type', TYPE[q.quiz_type] || q.quiz_type], ...(restrictions(q).length ? [['Restrictions', restrictions(q).join(' · ')]] : []), ['Available until', q.lock_at ? U.fmtAt(q.lock_at) : null]]),
        U.el('bcv-detail__actions', [
          q.locked_for_user ? U.badge(q.lock_explanation ? htmlToText(q.lock_explanation, 120) : 'Locked', 'orange')
            : noneLeft ? U.badge(`No attempts left · ${U.plural(limit.allowed, 'attempt')} allowed`, 'orange')
              : U.btn(open ? (/survey/.test(q.quiz_type || '') ? 'Continue survey' : 'Resume attempt') : (/survey/.test(q.quiz_type || '') ? 'Take the survey' : 'Take the quiz'), { kind: 'primary', icon: IC.bolt, iconColor: '#fff', onClick: () => app.go(takeHref) }),
          latest && held && !/survey/.test(q.quiz_type || '') ? U.badge('Results not released yet', '')
            : latest && q.hide_results !== 'always' && !/survey/.test(q.quiz_type || '') ? U.btn('See feedback', { kind: noneLeft && !q.locked_for_user ? 'primary' : '', icon: IC.check, iconColor: noneLeft && !q.locked_for_user ? '#fff' : undefined, onClick: () => app.go(feedbackHref(latest)) }) : null,
          q.locked_for_user ? null : U.btn('Open in Canvas', { icon: IC.external, onClick: () => app.go(nativeHref(`${c.url}/quizzes/${q.id}`)) }),
        ]),
        q.cant_go_back ? U.text('bcv-hint bcv-pretty', 'This quiz seals each question once you leave it: an answer cannot be changed and you cannot go back.') : null,
        q.description ? CS().prose(q.description) : U.text('bcv-hint', 'No instructions.'),
      ]), 'bcv-card--22'),
    );
    side.append(h('div', {}, [U.label('Attempts'), attemptRows.length ? U.card(attemptRows, 'bcv-card--list') : U.emptyCard('No attempts yet.')]));
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

  BCV.screens.courseDetail = D;
})();
