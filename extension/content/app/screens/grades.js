/* Grades tab, drawn to the mockup: one card with the nested rings (outer =
 * total, one ring per assignment group with graded work, 0%-weight rings
 * stippled) beside the total, a By-group legend and the weight bar, then
 * the assignment list. What-if mode recolours it all gray and never leaves
 * the browser; in it an assignment can be made up and put in any group (a
 * category), counted there with the group's weight. Pressing a group (its legend row, its weight, its ring) picks
 * it out: its ring, weight and assignments stay, the rest steps back. Each
 * graded assignment wears its letter, and the By letter slider (All · A · B ·
 * C · D · F) lists one letter's work alone. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;
  const NS = 'http://www.w3.org/2000/svg';

  const whatIfState = new Map(); // courseId -> { on, values, added: [{ id, groupId, name, possible }], seq, addGroup }
  // (2.99.145) the courses whose rings have filled from empty this page's life: once each, as the iPhone's do once a launch
  const filledOnce = new Set();
  const stillMotion = () => { try { return matchMedia('(prefers-reduced-motion: reduce)').matches; } catch { return false; } };

  // ---- standards (2.99.151) ------------------------------------------------------------------------------
  // A course graded on standards (store.standards): each standard with the level the student stands at — its name in its
  // level's colour, a bar filled to the score with a tick where mastery starts, the score — and, pressed, the work that
  // marked it, each piece with the level it gave. Shared with an assignment's page (its own standards).
  const ptsOf = (v) => String(Math.round(Number(v) * 100) / 100); // (to two places, as Canvas shows a level: 3.65, not 3.7)
  /** The bar: the score on the standard's whole range, in the level's colour, mastery's place ticked. */
  function stdBar(sd, score, level) {
    if (!(sd.possible > 0)) return null;
    const at = (v) => `${Math.max(0, Math.min(100, (Number(v) / sd.possible) * 100)).toFixed(1)}%`;
    return h('span', { class: 'bcv-std__bar', 'aria-hidden': 'true' }, [
      score === null || score === undefined ? null : h('span', { class: 'bcv-std__fill', style: { width: at(score), background: level?.color || 'var(--bcv-ink3)' } }),
      sd.mastery > 0 ? h('span', { class: 'bcv-std__tick', style: { left: at(sd.mastery) } }) : null,
    ]);
  }
  /** The level, in words, by a dot of its colour (a level at or past mastery says so to a screen reader too). */
  function stdLevel(level, met) {
    if (!level) return U.text('bcv-std__level is-none', 'Not assessed yet', 'span');
    return h('span', { class: `bcv-std__level ${met ? 'is-met' : ''}`, style: { '--bcv-lvl': level.color }, title: met ? `${level.description} — mastery met` : level.description }, [
      h('span', { class: 'bcv-std__dot', 'aria-hidden': 'true' }), h('span', { class: 'bcv-std__word', text: level.description || ptsOf(level.points) }),
    ]);
  }
  const stdScore = (sd, score) => (score === null || score === undefined || sd.hidePoints ? null : U.text('bcv-std__score', `${ptsOf(score)} / ${ptsOf(sd.possible)}`, 'span'));
  /** One standard: a row that opens to the work that marked it. */
  function stdRow(sd, open) {
    const id = `bcv-std-${sd.id}`;
    const shut = !open.has(sd.id);
    const work = sd.results.length ? U.el('bcv-std__work', sd.results.map((r) => U.row([
      U.el('bcv-row__body', [U.text('bcv-std__wname bcv-pretty', r.name, 'span'), r.at ? U.text('bcv-row__sub', `marked ${U.fmtAt(r.at)}`) : null]),
      stdLevel(r.level, r.met),
      stdScore(sd, r.score),
      r.url ? U.chev() : null,
    ], { href: r.url || null, cls: 'bcv-std__wrow' }))) : null;
    if (work) { work.id = id; work.hidden = shut; }
    const head = h(work ? 'button' : 'div', { class: 'bcv-std__head', ...(work ? { type: 'button', 'aria-expanded': String(!shut), 'aria-controls': id, onclick: (e) => {
      const btn = e.currentTarget;
      const now = btn.getAttribute('aria-expanded') !== 'true';
      btn.setAttribute('aria-expanded', String(now));
      work.hidden = !now;
      if (now) open.add(sd.id); else open.delete(sd.id);
    } } : {}) }, [
      U.el('bcv-std__body', [
        U.text('bcv-std__name bcv-pretty', sd.title, 'span'),
        U.text('bcv-std__sub', [sd.group, sd.score === null ? null : U.plural(sd.count || sd.results.length || 1, 'assessment'), sd.mastery ? `mastery at ${ptsOf(sd.mastery)}` : null].filter(Boolean).join(' · '), 'span'),
      ]),
      stdBar(sd, sd.score, sd.level),
      stdLevel(sd.level, sd.met),
      stdScore(sd, sd.score),
      work ? h('span', { class: 'bcv-std__chev', 'aria-hidden': 'true' }, [U.svg('M9 6l6 6-6 6', { size: 14, stroke: 'currentColor', width: 2.2 })]) : null,
    ]);
    return h('div', { class: `bcv-std ${sd.met ? 'is-met' : ''}`, dataset: { std: sd.id } }, [head, work]);
  }
  /** The Grades tab's card: what the standards are, how many are met, each standard. */
  function standardsSec(std, open) {
    const sub = std.assessed ? `${std.met} of ${std.assessed} met` : 'none assessed yet';
    return U.el('bcv-gr__sec bcv-gr__sec--std', [
      U.el('bcv-gr__hrow', [U.text('bcv-gr__h', 'Standards', 'span'), U.text('bcv-gr__hsub', sub, 'span')]),
      U.text('bcv-gr__wnote bcv-pretty', 'This course marks work against standards. Each shows the level you’re at now; press one to see the work that marked it.'),
      U.el('bcv-std__list', std.list.map((sd) => stdRow(sd, open))),
    ]);
  }
  /** An assignment's row: how it did on the standards it was marked against, in a few words. */
  function stdLine(std, assignmentId) {
    if (!std) return null;
    const got = std.list.flatMap((sd) => sd.results.filter((r) => r.assignmentId && String(r.assignmentId) === String(assignmentId)));
    if (!got.length) return null;
    const met = got.filter((r) => r.met).length;
    return `${U.plural(got.length, 'standard')}, ${met} met`;
  }

  async function render(ctx, shell) {
    const c = shell.course;
    const dark = shell.dark;
    const b = U.el('bcv-body bcv-body--course-cols');
    b.append(U.loading());
    const groups = await store.assignmentGroups(c.id, { maxAge: store.freshness.grades }).catch(() => null); // never a score older than the freshness: a tool may have posted one since
    if (!ctx.alive()) return b;
    if (!groups) return b.replaceChildren(U.errorBox('Grades could not be loaded.')) || b;
    const st = whatIfState.get(c.id) || { on: false, values: {}, added: [], seq: 0, addGroup: null };
    whatIfState.set(c.id, st);
    // the course's groups, as the what-if's "add an assignment" offers them (the categories the grade is weighted by)
    const cats = groups.map((g) => ({ id: String(g.id), name: g.name, weight: Number(g.group_weight) || 0 }));
    const whatIfAllowed = (await store.pref('whatIfScores', true)) !== false; // Settings → Grades → Show what-if scores
    // (2.99.151) a course graded on standards: the student's level on each, asked for beside the scores and drawn in when
    // it lands (most courses have none, and the page is not held for it)
    let std = null;
    let drawn = false; // (the page's first drawing made: the standards landing before it are simply in it)
    const stdOpen = new Set(); // (the standards opened to their work, kept across redraws)
    store.standards(c.id, { maxAge: store.freshness.grades }).then((got) => { if (!got || !ctx.alive()) return; std = got; if (drawn) draw(); }).catch(() => {});
    // (2.99.145) the course's grading scale — its own, the student's, or the usual — for the letter beside the total
    const G = BCV.screens.gpa;
    try { await G?.loadScales?.(); } catch { /* (the usual scale, then) */ }
    if (!whatIfAllowed) st.on = false;
    // (2.98.59) the student's own weights for this course — a percent per group, from the syllabus,
    // for a course Canvas leaves unweighted or weighs another way — kept in the site's preferences
    // by course id and never sent to Canvas; the rings, the total and the GPA page follow them
    const ownPref = await store.pref('gradeWeights', {});
    let own = ownPref && typeof ownPref === 'object' && ownPref[c.id] && typeof ownPref[c.id] === 'object' && Object.keys(ownPref[c.id]).length ? { ...ownPref[c.id] } : null;
    const wEdit = { on: false, vals: {}, focus: false };
    let focusId = null;
    // the group picked out (U.groupPicker), kept across what-if redraws; the list's header says which
    const pickSt = { pick: null };
    const bandSt = { band: 'all' }; // the By letter slider's stop, kept across redraws
    let gm = null;
    const pickNote = h('span', { class: 'bcv-group__sub bcv-ml-auto bcv-gr__picknote' });
    function notePick(pick) {
      // in what-if mode the group pressed is the one a made-up assignment goes into
      const addSel = b.querySelector('.bcv-wfadd__group');
      if (pick && addSel && addSel.value !== pick && [...addSel.options].some((o) => o.value === pick)) { addSel.value = pick; addSel.dispatchEvent(new Event('change')); }
      const name = pick && gm ? (gm.legend.find((r) => r.id === pick)?.label ?? gm.ungraded.find((u) => u.id === pick)?.name) : null;
      if (!name) { pickNote.replaceChildren('By group, each by due date'); return; }
      const n = gm.rows.filter((r) => r.groupId === pick).length;
      pickNote.replaceChildren(h('button', { type: 'button', class: 'bcv-gr__pickchip', 'data-pick': '', style: { '--bcv-grp': colorOf(pick) }, 'aria-label': `Showing ${name}; show every assignment` }, [
        h('span', { class: 'bcv-gr__pickdot' }),
        U.text('bcv-gr__pickname', `${name} · ${U.plural(n, 'assignment')}`, 'span'),
        U.text('bcv-gr__pickall', 'Show all', 'span'),
      ]));
    }
    const colorOf = (id) => gm?.legend.find((r) => r.id === id)?.color || gm?.weightBar.find((w) => w.id === id)?.color || null;
    const picker = U.groupPicker(b, pickSt, { onChange: notePick });

    let shownTotal = null; // (the figure last drawn: a new one settles in, an unchanged one stays still)
    function draw() {
      gm = store.gradeModel(groups, c, st.values, st.on, dark, st.added, { ownWeights: own });
      // (2.99.145, as the Mac's and the iPhone's since 2.99.33) What-If starts from Canvas's own total, not from Simpl's
      // reading of the scores: turned on with nothing typed the grade stays the one the course shows (92.4% A− stayed
      // 92.4% A−, where it jumped to 100%), and a score tried moves it by what that score changes in Simpl's reading
      const lms = BCV.lms.name;
      const typed = Object.keys(st.values).length > 0 || st.added.length > 0;
      const canvasTotal = c.score !== null && c.score !== undefined && !own && !c.hideFinal ? store.roundHalfUp(Number(c.score), 1) : null;
      let total = gm.total === null || gm.total === undefined ? null : Number(gm.total);
      let note = gm.center.note;
      let estimate = '';
      let fromCanvas = !st.on;
      if (st.on && canvasTotal !== null) {
        const base = store.gradeModel(groups, c, {}, true, dark, [], { ownWeights: own }).total;
        const gap = base !== null && base !== undefined ? store.roundHalfUp(Math.abs(store.roundHalfUp(base, 1) - canvasTotal), 1) : 0;
        if (gap > 0) estimate = `Simpl’s own estimate differs from ${lms}’s total by ${store.fmtPts(gap)} ${gap === 1 ? 'point' : 'points'}.`;
        if (!typed) { total = canvasTotal; fromCanvas = true; note = `${lms}’s total. Try a score to see where it would land.`; }
        else if (base !== null && base !== undefined && total !== null) { total = Math.max(0, store.roundHalfUp(canvasTotal + (total - Number(base)), 1)); note = `${lms}’s total, moved by the scores you tried.`; }
      }
      const totalText = total === null ? '—' : `${store.fmtPts(total)}%`;
      // the letter: Canvas's own beside its own total, else the course's scale's for the figure shown
      const S = G?.scaleOf ? G.scaleOf(c.id) : G?.SCALE;
      const canvasLetter = fromCanvas && c.grade && !own && !G?.hasScale?.(c.id) ? String(c.grade).trim() : null;
      let letter = total === null ? null : canvasLetter || (S && G?.letterFor ? G.letterFor(total, S)[0] : null);
      if (letter && /^[A-F][+\-−]?$/i.test(letter)) letter = letter.toUpperCase().replace('-', '−');
      const letterBand = letter && /^[A-F]/.test(letter) ? letter[0] : null;
      const parts = [];
      if (st.on) {
        parts.push(U.el('bcv-banner', [
          U.svg(IC.warn, { size: 24, stroke: 'var(--bcv-red)', width: 2, style: { flex: 'none' } }),
          h('div', { style: { flex: '1', minWidth: '200px' } }, [U.text('bcv-banner__title', 'This is not your actual score.'), U.text('bcv-banner__sub', 'What-if mode — your real scores are loaded in; edit any of them, or add an assignment to a category, to test outcomes. Nothing is saved or sent to your instructor.')]),
          U.btn('Clear all what-if scores', { kind: 'danger', onClick: () => { st.values = {}; st.added = []; draw(); } }),
        ]));
      }

      // ---- rings + total ---------------------------------------------------------
      const svg = ns('svg');
      svg.setAttribute('viewBox', '0 0 160 160');
      svg.setAttribute('class', 'bcv-rings__svg');
      if (gm.stipples.length) {
        const defs = ns('defs');
        for (const p of gm.stipples) {
          const pat = ns('pattern');
          pat.setAttribute('id', p.id);
          pat.setAttribute('patternUnits', 'userSpaceOnUse');
          pat.setAttribute('width', '3');
          pat.setAttribute('height', '3');
          const dot = ns('circle');
          dot.setAttribute('cx', '1.5');
          dot.setAttribute('cy', '1.5');
          dot.setAttribute('r', '0.72');
          dot.setAttribute('fill', p.color);
          pat.append(dot);
          defs.append(pat);
        }
        svg.append(defs);
      }
      // each group's ring is that group's (pressed, it picks the group out); the outer one is the total
      const ringOf = (el, r) => {
        el.setAttribute('data-group', r.id || 'total');
        if (r.id) {
          el.setAttribute('data-pick', r.id);
          const t = ns('title');
          t.textContent = gm.legend.find((x) => x.id === r.id)?.label || '';
          el.append(t);
        }
        return el;
      };
      const rings = gm.rings.map((r, i) => {
        if (i !== 0 || total === gm.total) return r;
        const C = 2 * Math.PI * r.r;
        const pct = total === null ? 0 : Math.min(total, 100);
        const filled = (pct / 100) * C;
        return { ...r, dash: `${filled.toFixed(1)} ${(C - filled).toFixed(1)}`, cap: pct > 0 ? 'round' : 'butt', arc: pct > 0 ? r.color : 'transparent' };
      });
      for (const r of rings) svg.append(ringOf(circle(r.r, r.track, null, null, r.w), r));
      for (const r of rings) { const arc = ringOf(circle(r.r, r.arc, r.cap, r.dash, r.w), r); arc.dataset.arc = String(r.r); svg.append(arc); }
      // (2.99.145) the total's card, as a summary: the rings, then the figure — the one thing to read first, its letter
      // beside it rather than in the small print — and what the figure is; What-If by it, the control by the figure it
      // changes (at the card's foot when narrow, at its end when wide — app.css)
      const center = U.el('bcv-gr__center', [
        U.text('bcv-gr__label', gm.center.label),
        h('div', { class: 'bcv-gr__fig' }, [
          h('span', { class: 'bcv-gr__total', text: totalText }),
          letter ? h('span', { class: `bcv-gr__letter ${letterBand ? `bcv-band--${letterBand}` : ''} ${st.on && !fromCanvas ? 'is-hyp' : ''}`, text: letter, title: st.on && !fromCanvas ? `${letter} with what-if` : letter }) : null,
        ]),
        U.text('bcv-gr__note bcv-pretty', note),
        estimate ? U.text('bcv-gr__estimate bcv-pretty', estimate) : null,
      ]);
      const totalFoot = whatIfAllowed ? U.el('bcv-gr__foot', [
        h('button', { type: 'button', class: `bcv-whatif-btn ${st.on ? 'is-on' : ''}`, 'aria-pressed': String(st.on), text: st.on ? 'Exit what-if mode' : 'Try what-if scores', onclick: () => { st.on = !st.on; draw(); } }),
        U.text('bcv-gr__foothint', st.on ? 'Nothing is saved or sent.' : 'Nothing you try is saved.', 'span'),
      ]) : null;

      // ---- the groups ----------------------------------------------------------------
      // (2.99.143: a group's dot is its colour whether or not it has a ring — five rings at most — as in the weight bar)
      const legendRow = (r) => U.el('bcv-legend__row', [
        h('span', { class: 'bcv-legend__dot', style: r.zero
          ? { background: `radial-gradient(${r.color} 34%, transparent 36%) 0 0 / 2.4px 2.4px` }
          : { background: r.color } }),
        U.el('bcv-legend__body', [U.text('bcv-legend__label', r.label, 'span'), U.text('bcv-legend__detail', r.detail, 'span')]),
        // (2.99.145) one column of figures: the group's score, and under it what it weighs — two right-hand columns read
        // as a table with no head
        U.el('bcv-legend__num', [
          U.text(`bcv-legend__value ${r.zero ? 'bcv-legend__value--muted' : ''}`, r.value, 'span'),
          gm.weighted ? U.text('bcv-legend__weight', r.weightText, 'span') : null,
        ]),
      ], U.groupAttrs(r.id, r.color, { pick: true, name: r.label }));
      const ungradedRow = (u) => U.el('bcv-legend__row bcv-legend__row--ungraded', [
        h('span', { class: 'bcv-legend__dot' }),
        U.el('bcv-legend__body', [U.text('bcv-legend__label bcv-ungraded__name', u.name, 'span'), U.text('bcv-legend__detail', 'Nothing graded yet', 'span')]),
        U.el('bcv-legend__num', [
          U.text('bcv-legend__value', '—', 'span'),
          u.weightText ? U.text('bcv-legend__weight', u.weightText === '0%' ? 'not weighted' : `${u.weightText} of grade`, 'span') : null,
        ]),
      ], U.groupAttrs(u.id, null, { pick: true, name: u.name }));
      const groupRows = U.el('bcv-gr__list', [
        ...gm.legend.map(legendRow),
        ...gm.ungraded.map(ungradedRow),
        gm.legend.length || gm.ungraded.length ? null : U.text('bcv-gr__wnote', 'No assignment groups in this course.'),
      ]);
      // (2.99.145: a wide card lists the groups in two columns, down the first and then the second, in their order)
      groupRows.style.setProperty('--bcv-gr-rows', String(Math.max(1, Math.ceil((gm.legend.length + gm.ungraded.length) / 2))));

      // ---- how the grade is weighted ------------------------------------------------
      // (2.98.59) and a button to weight the groups yourself, from the syllabus; the editor takes the section's place
      let weightsSec;
      const ownBtn = h('button', { type: 'button', class: `bcv-gr__ownbtn ${own ? 'is-on' : ''}`, text: own ? 'Edit my weights' : 'Use my own weights', title: `Weight the groups yourself, from the syllabus — on this device only, never sent to ${BCV.lms.name}`, onclick: () => {
        wEdit.on = true;
        wEdit.focus = true;
        wEdit.vals = Object.fromEntries(groups.map((g) => [String(g.id), own ? (own[String(g.id)] ?? 0) : (Number(g.group_weight) || 0)]));
        draw();
      } });
      // (2.99.143) one list, as the Mac's Assignment Groups card: its head (what the weights add up to), the weight bar
      // over the groups' own lines — each its weight and its score — and what is worth saying under them. (By group and
      // How the grade is weighted listed every group twice, one card over the other.) The editor takes the list's place.
      if (wEdit.on) weightsSec = weightEditor();
      else {
        weightsSec = U.el('bcv-gr__sec bcv-gr__sec--w', [
          U.el('bcv-gr__hrow', [U.text('bcv-gr__h', 'Assignment groups', 'span'), U.text('bcv-gr__hsub', !gm.weighted ? 'by points' : gm.own ? `${gm.weightSum}% · your own weights` : `${gm.weightSum}% of final grade`, 'span')]),
          gm.weighted && gm.weightBar.length ? U.el('bcv-wbar', gm.weightBar.map((w) => { const ga = U.groupAttrs(w.id, w.color, { pick: true, name: w.name }); return h('div', { ...ga, class: `bcv-wbar__seg ${w.graded ? '' : 'bcv-wbar__seg--ungraded'}`, style: { ...ga.style, flex: `${w.weight} 1 0`, background: w.color || '' }, title: `${w.name} · ${w.weight}%` }); })) : null,
          groupRows,
          gm.weighted && !gm.weightBar.length ? U.text('bcv-gr__wnote bcv-pretty', 'No assignment group carries weight yet.') : null,
          gm.weighted ? null : h('p', { class: 'bcv-gr__wnote bcv-pretty', text: 'This course does not weight assignment groups: the total is points earned over points possible.' }),
          gm.weightNote ? h('p', { class: 'bcv-gr__wnote bcv-pretty', text: gm.weightNote }) : null,
          gm.own ? h('p', { class: 'bcv-gr__wnote bcv-gr__wnote--own bcv-pretty', text: `Your own weights, from the syllabus — not your instructor’s. They live on this device and never reach ${BCV.lms.name}.` }) : null,
          ownBtn,
        ]);
      }
      /** The editor: a percent per group (every group, Canvas's figure beside it when it has one),
       *  the sum kept in view, Save / Cancel, and a way back to Canvas's weights once your own are on. */
      function weightEditor() {
        const num = (v) => { const n = parseFloat(String(v).replace(/[^0-9.]/g, '')); return Number.isFinite(n) && n >= 0 ? n : 0; };
        const sumEl = U.text('bcv-gr__wsum', '', 'span');
        const paintSum = () => {
          const s = Object.values(wEdit.vals).reduce((a, v) => a + num(v), 0);
          const whole = Math.abs(s - 100) < 0.05;
          sumEl.textContent = whole ? 'Total 100%' : `Total ${store.fmtPts(Math.round(s * 10) / 10)}% · shares taken in proportion`;
          sumEl.classList.toggle('is-off', !whole);
        };
        const save = async () => {
          const map = {};
          for (const [id, v] of Object.entries(wEdit.vals)) { const n = num(v); if (n > 0) map[id] = n; }
          if (!Object.keys(map).length) { U.toast(`Give at least one group a weight — or go back to ${BCV.lms.name}’s.`, { error: true }); return; }
          own = map;
          wEdit.on = false;
          await store.mergePref('gradeWeights', { [c.id]: map });
          draw();
          U.toast(`Your weights are on for this course. Nothing is sent to ${BCV.lms.name}.`);
        };
        const cancel = () => { wEdit.on = false; draw(); };
        const clear = async () => {
          own = null;
          wEdit.on = false;
          await store.mergePref('gradeWeights', { [c.id]: null });
          draw();
          U.toast(`Back to the weights ${BCV.lms.name} carries.`);
        };
        const rows = groups.map((g) => {
          const id = String(g.id);
          const inp = h('input', { type: 'text', inputmode: 'decimal', class: 'bcv-input bcv-gr__winput', value: String(wEdit.vals[id] ?? 0), 'aria-label': `Weight of ${g.name}`, dataset: { wgroup: id } });
          inp.addEventListener('input', () => { wEdit.vals[id] = inp.value; paintSum(); });
          inp.addEventListener('focus', () => inp.select());
          inp.addEventListener('keydown', (e) => { if (e.key === 'Enter') save(); if (e.key === 'Escape') cancel(); });
          return U.el('bcv-gr__wedit-row', [
            h('span', { class: 'bcv-wbar__dot', style: { background: colorOf(id) || 'var(--bcv-fill2)' } }),
            U.text('bcv-wbar__name', g.name, 'span'),
            c.weighted ? U.text('bcv-gr__wcanvas', `${BCV.lms.name}: ${Number(g.group_weight) || 0}%`, 'span') : null,
            inp,
            U.text('bcv-gr__wpct', '%', 'span'),
          ]);
        });
        paintSum();
        return U.el('bcv-gr__sec bcv-gr__sec--w bcv-gr__wedit', [
          U.el('bcv-gr__hrow', [U.text('bcv-gr__h', 'My own weights', 'span'), sumEl]),
          U.text('bcv-gr__wnote bcv-pretty', `A percent per group, as your syllabus gives them. The total here follows them; nothing is sent to ${BCV.lms.name} or your instructor.`),
          ...rows,
          U.el('bcv-gr__wbtns', [own ? U.btn(`Back to ${BCV.lms.name}’s weights`, { kind: 'danger', cls: 'bcv-gr__wclear', onClick: clear }) : null, h('span', { class: 'bcv-gr__wspace' }), U.btn('Cancel', { cls: 'bcv-gr__wcancel', onClick: cancel }), U.btn('Save', { kind: 'primary', cls: 'bcv-gr__wsave', onClick: save })]),
        ]);
      }
      // (2.99.143) two cards: the rings and the total in one, the assignment groups in the other. (2.99.145) One over the
      // other at every width — the total a banner across the page when wide, the groups under it in two columns — so
      // neither card is left short beside the other or stretched to its height
      const ringsCard = U.el('bcv-gr__cards', [
        U.card(U.el('bcv-gr bcv-gr--total', [U.el('bcv-gr__top', [svg, center]), totalFoot]), 'bcv-card--22 bcv-gr__card'),
        U.card(U.el('bcv-gr bcv-gr--groups', [weightsSec]), 'bcv-card--22 bcv-gr__card'),
        std ? U.card(U.el('bcv-gr bcv-gr--groups bcv-gr--std', [standardsSec(std, stdOpen)]), 'bcv-card--22 bcv-gr__card bcv-gr__card--std') : null,
      ]);

      // ---- assignments ----------------------------------------------------------------
      const rows = gm.rows.map((g) => {
        let scoreEl;
        if (!st.on) {
          scoreEl = h('span', { class: 'bcv-grade__score', title: 'Double-click to test a what-if score', text: `${g.grade ? `${g.grade} · ` : ''}${g.earned === null ? '—' : store.fmtPts(g.earned)} / ${store.fmtPts(g.possible)}` }); // (a letter or pass/fail grade first, as Canvas names it)
          // the row opens the assignment, so the score has to keep its own presses: otherwise the
          // first half of the double-click that starts a what-if would navigate away instead
          scoreEl.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); });
          scoreEl.addEventListener('dblclick', () => { st.on = true; focusId = g.id; draw(); });
        } else {
          const input = h('input', { type: 'text', inputmode: 'decimal', placeholder: '—', class: `bcv-whatif__input ${g.hypothetical ? 'is-hyp' : ''}`, dataset: { wf: g.id }, value: st.values[g.id] === undefined ? (g.earned === null ? '' : String(g.earned)) : st.values[g.id] });
          // (2.99.142: the caret back in the field it went to after the redraw; Tab from score to score — ui.whatIfField)
          U.whatIfField(input, { take: (v) => { st.values[g.id] = v; }, redraw: (next) => { if (!ctx.alive()) return; focusId = next; draw(); }, scope: () => b });
          scoreEl = U.el('bcv-whatif', [input, U.text('bcv-whatif__possible', `/ ${store.fmtPts(g.possible)}`, 'span')]);
          scoreEl.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); }); // typing a what-if is not opening the row
        }
        // the whole row opens the assignment it is a line about — a grade is the start of a question
        // ("why?"), and the answer is on the assignment's own page
        // its letter (U.gradeBand: what-if scores included), a tag in the letter's colour by the score
        const band = U.gradeBand(g.effective, g.possible, g.gradingType);
        const row = U.row([
          U.el('bcv-row__body', [
            h('div', { style: { display: 'flex', alignItems: 'baseline', gap: '8px', flexWrap: 'wrap' } }, [U.text('bcv-grade__name bcv-pretty', g.name, 'span'), g.added ? U.badge('What-if', 'red', 'bcv-badge--xs') : g.badge ? U.badge(g.badge, g.badge === 'Late' ? 'orange' : g.badge === 'Missing' ? 'red' : '', 'bcv-badge--xs') : null]),
            // the group, the dates, and — on a marked assignment — how the class did (Canvas's own mean, high and low)
            // (2.99.36: the group is the section's head now, not said again in each row)
            // (2.99.145: the class's high and low are the mark beside the score; its mean stays in words)
            U.text('bcv-row__sub', g.added ? `made up to test, not in ${BCV.lms.name}` : [g.due ? `due ${U.fmtBy(g.due)}` : 'no due date', g.submitted ? `submitted ${U.fmtAt(g.submitted)}` : (g.due && U.parse(g.due) > new Date() ? 'not due yet' : 'not submitted'), g.stats && g.earned !== null ? `class mean ${store.fmtPts(g.stats.mean)}` : null, stdLine(std, g.id)].filter(Boolean).join(' · ')),
          ]),
          spread(g, band),
          U.bandChip(band, band ? `${store.fmtPts(Math.round((g.effective / g.possible) * 1000) / 10)}% · ${band}` : null),
          scoreEl,
          g.added ? U.whatIfRemove(g.name, () => { st.added = st.added.filter((x) => x.id !== g.id); delete st.values[g.id]; draw(); }) : null,
          g.url ? U.chev() : null,
        ], { href: g.url || null });
        if (g.added) row.classList.add('is-wfadded');
        row.dataset.band = band || '';
        row.dataset.group = g.groupId; // (the group it counts toward: picked out with it)
        row.style.setProperty('--bcv-grp', colorOf(g.groupId) || 'var(--bcv-ink3)');
        return row;
      });
      // By letter: the slider lists one letter's work alone (the rows are the same; only the list is refilled)
      const counts = {};
      for (const r of rows) if (r.dataset.band) counts[r.dataset.band] = (counts[r.dataset.band] || 0) + 1;
      const listHost = h('div');
      // (2.99.36) the work partitioned by its group (the course's categories, in Canvas's order): each group under a
      // head of its own — its colour, its name, its weight and its score so far — rather than one list with the
      // group's name in each row's small print. Pressing a head picks the group out, as its legend row does.
      const groupSections = (shown) => {
        const order = groups.map((g) => String(g.id));
        const byId = new Map();
        for (const r of shown) {
          const id = r.dataset.group || '';
          if (!byId.has(id)) byId.set(id, []);
          byId.get(id).push(r);
        }
        const ids = [...order.filter((id) => byId.has(id)), ...[...byId.keys()].filter((id) => !order.includes(id))];
        return ids.map((id) => {
          const g = groups.find((x) => String(x.id) === id);
          const lg = gm.legend.find((x) => x.id === id);
          const name = g?.name || lg?.label || 'Other';
          const n = byId.get(id).length;
          // (its weight as the grade counts it — the student's own weights where set: the legend's, else the weight bar's)
          const wb = gm.weightBar.find((w) => w.id === id);
          const weight = !gm.weighted ? '' : lg?.weightText || (wb ? `${wb.weight}% of grade` : g ? 'not weighted' : '');
          const head = h('div', { ...U.groupAttrs(id, colorOf(id), { pick: !!g, name }), class: 'bcv-gr__ghead' }, [
            h('span', { class: 'bcv-gr__gdot', 'aria-hidden': 'true' }),
            U.text('bcv-gr__gname bcv-pretty', name, 'span'),
            U.text('bcv-gr__gmeta', [weight, `${n} ${n === 1 ? 'item' : 'items'}`].filter(Boolean).join(' · '), 'span'),
            U.text('bcv-gr__gscore', lg ? `${lg.detail.split(' · ')[0]} · ${lg.value}` : 'not graded yet', 'span'),
          ]);
          return h('section', { class: 'bcv-gr__gsec', 'aria-label': name }, [head, U.card(byId.get(id), 'bcv-card--list')]);
        });
      };
      const paintList = () => {
        const band = bandSt.band;
        const shown = band === 'all' ? rows : rows.filter((r) => r.dataset.band === band);
        // (2.99.143) the groups in a column of their own, which a wide page flows into two, balanced, in their order
        // (app.css: .bcv-gr__gcols) — the Mac's Grades section has had them side by side since 1.2
        listHost.replaceChildren(...(!rows.length ? [U.emptyCard('No assignments in this course.')]
          : shown.length ? [h('div', { class: 'bcv-gr__gcols' }, groupSections(shown))]
            : [U.emptyCard(`No ${band} grades in this course${st.on ? ' with these what-if scores' : ''}.`)]));
      };
      paintList();
      // what-if: an assignment made up and put in a group of the student's choosing, counted there like the rest
      const adder = st.on && cats.length ? U.whatIfAdder(cats, {
        group: st.addGroup ?? pickSt.pick, weighted: gm.weighted, n: st.seq + 1,
        onGroup: (id) => { st.addGroup = id; },
        onAdd: ({ groupId, name, score, possible }) => {
          st.seq += 1;
          const id = `wf-${st.seq}`;
          st.added.push({ id, groupId, name, possible });
          st.values[id] = String(score);
          st.addGroup = groupId;
          bandSt.band = 'all'; // (the new row in view, whatever letter it lands on)
          draw();
          b.querySelector('.bcv-wfadd__name')?.focus(); // (ready for the next)
        },
      }) : null;
      const tableCard = h('div', {}, [
        U.el('bcv-group__head bcv-group__head--10', [U.h2('Assignments'), pickNote]),
        adder,
        Object.keys(counts).length ? U.el('bcv-gr__bandrow', [
          U.text('bcv-gr__bandlbl', 'By letter', 'span'),
          U.bandSlider(counts, bandSt.band, (b) => { bandSt.band = b; paintList(); picker.apply(); }),
        ]) : null,
        listHost,
      ]);

      // (2.99.145) each ring as it stands on screen now — mid-glide if it is still moving — to glide on from
      const was = new Map([...b.querySelectorAll('.bcv-rings__svg > circle[data-arc]')].map((el) => [el.dataset.group, getComputedStyle(el).strokeDasharray]));
      // The weights live in the grade card's "How the grade is weighted" section; no separate card.
      b.replaceChildren(...parts, U.el('bcv-grades__main', [ringsCard, tableCard]));
      settle(svg, was, center.querySelector('.bcv-gr__fig'), shownTotal !== null && shownTotal !== totalText);
      shownTotal = totalText;
      if (pickSt.pick && !gm.rows.some((r) => r.groupId === pickSt.pick) && !gm.legend.some((r) => r.id === pickSt.pick) && !gm.ungraded.some((u) => u.id === pickSt.pick)) pickSt.pick = null;
      picker.apply();
      if (focusId) {
        const el = b.querySelector(`[data-wf="${CSS.escape(focusId)}"]`);
        if (el) { el.focus(); el.select(); }
        focusId = null;
      }
      if (wEdit.on && wEdit.focus) { // (the editor just opened: the first weight ready to type over)
        wEdit.focus = false;
        const first = b.querySelector('.bcv-gr__winput');
        if (first) { first.focus(); first.select(); }
      }
    }
    /** (2.99.145) The rings move as the scores do: on the course's first showing this page's life they fill from
     *  empty; after, each ring glides from where it stands on screen to its new figure (a what-if typed, a weight
     *  saved) — from the drawn value, so a second change mid-glide carries on from there rather than jumping. On the
     *  app's own springs (motion.js: no overshoot). The figure settles in when it changes. With reduced motion, none of
     *  it: the new figures simply show. */
    function settle(svgEl, was, fig, changed) {
      const first = !filledOnce.has(c.id);
      filledOnce.add(c.id);
      const M = BCV.motion;
      if (stillMotion() || typeof Element.prototype.animate !== 'function') return;
      const glide = (el, from, to, preset) => el.animate([{ strokeDasharray: from }, { strokeDasharray: to }], {
        duration: Math.round((M?.duration?.(preset) || 0.42) * 1000),
        easing: M?.cssEasing ? M.cssEasing(preset) : 'cubic-bezier(.23,1,.32,1)',
      });
      const nums = (v) => String(v).replace(/px|,/g, ' ').trim().split(/\s+/).map(Number).join(' ');
      const moves = [];
      for (const el of svgEl.querySelectorAll('circle[data-arc]')) {
        const to = el.getAttribute('stroke-dasharray');
        if (!to) continue;
        const zero = `0 ${(2 * Math.PI * Number(el.dataset.arc)).toFixed(1)}`;
        // (a ring new to the card — a group's first what-if score — fills from empty)
        const from = was.get(el.dataset.group) || (first || was.size ? zero : null);
        if (!from || from === 'none' || nums(from) === nums(to)) continue;
        moves.push([el, from, to]);
      }
      if (!moves.length) { if (changed && fig) fig.animate([{ opacity: 0.4, transform: 'translateY(3px)' }, { opacity: 1, transform: 'none' }], { duration: 220, easing: 'cubic-bezier(.23,1,.32,1)' }); return; }
      if (svgEl.isConnected) {
        for (const [el, from, to] of moves) glide(el, from, to, was.size ? 'morph' : 'gentle');
        if (changed && fig) fig.animate([{ opacity: 0.4, transform: 'translateY(3px)' }, { opacity: 1, transform: 'none' }], { duration: 220, easing: 'cubic-bezier(.23,1,.32,1)' });
        return;
      }
      // the first drawing is made before the page holds it, and an animation on a node not yet in the page does not
      // run: hold each ring empty until the page has it, then fill — in the same frame, so it never shows full first
      for (const [el, from] of moves) el.style.strokeDasharray = from;
      let tries = 0;
      const wait = () => {
        if (svgEl.isConnected || ++tries > 90) {
          for (const [el, from, to] of moves) { el.style.strokeDasharray = ''; if (svgEl.isConnected) glide(el, from, to, 'gentle'); }
          return;
        }
        requestAnimationFrame(wait);
      };
      requestAnimationFrame(wait);
    }
    /** (2.99.145) Where the score sits in the class: the class's low to high as a bar on the assignment's whole range,
     *  the mean a tick, the student's own score a dot in its letter's colour — read at a glance, the figures on hover. */
    function spread(g, band) {
      const st2 = g.stats;
      const you = g.effective ?? g.earned;
      if (!st2 || g.earned === null || !(g.possible > 0) || ![st2.min, st2.max, st2.mean].every((v) => Number.isFinite(Number(v))) || you === null || you === undefined) return null;
      const at = (v) => `${Math.max(0, Math.min(100, (Number(v) / g.possible) * 100)).toFixed(1)}%`;
      const said = `Class low ${store.fmtPts(st2.min)}, mean ${store.fmtPts(st2.mean)}, high ${store.fmtPts(st2.max)} — you ${store.fmtPts(you)} of ${store.fmtPts(g.possible)}`;
      return h('span', { class: `bcv-gr__spread ${band ? `bcv-band--${band}` : ''}`, role: 'img', 'aria-label': said, title: said }, [
        h('span', { class: 'bcv-gr__spread-range', style: { left: at(st2.min), width: `max(2px, calc(${at(st2.max)} - ${at(st2.min)}))` } }),
        h('span', { class: 'bcv-gr__spread-mean', style: { left: at(st2.mean) } }),
        h('span', { class: 'bcv-gr__spread-you', style: { left: at(you) } }),
      ]);
    }
    function ns(tag) {
      return document.createElementNS(NS, tag);
    }
    function circle(r, stroke, cap, dash, width = 12) {
      const el = ns('circle');
      el.setAttribute('cx', '80');
      el.setAttribute('cy', '80');
      el.setAttribute('r', r);
      el.setAttribute('fill', 'none');
      el.setAttribute('stroke', stroke);
      el.setAttribute('stroke-width', String(width));
      if (cap) el.setAttribute('stroke-linecap', cap);
      if (dash) el.setAttribute('stroke-dasharray', dash);
      return el;
    }
    draw();
    drawn = true;
    return b;
  }

  BCV.screens.grades = { render, stdRow, stdLevel, stdScore };
})();
