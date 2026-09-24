/* Grades tab, drawn to the mockup: one card with the nested rings (outer =
 * total, one ring per assignment group with graded work, 0%-weight rings
 * stippled) beside the total, a By-group legend and the weight bar, then
 * the assignment list. What-if mode recolours it all gray and never leaves
 * the browser. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;
  const NS = 'http://www.w3.org/2000/svg';

  const whatIfState = new Map(); // courseId -> { on, values }

  async function render(ctx, shell) {
    const c = shell.course;
    const dark = shell.dark;
    const b = U.el('bcv-body bcv-body--course-cols');
    b.append(U.loading());
    const groups = await store.assignmentGroups(c.id, { maxAge: store.freshness.grades }).catch(() => null); // never a score older than the freshness: a tool may have posted one since
    if (!ctx.alive()) return b;
    if (!groups) return b.replaceChildren(U.errorBox('Grades could not be loaded.')) || b;
    const st = whatIfState.get(c.id) || { on: false, values: {} };
    whatIfState.set(c.id, st);
    const whatIfAllowed = (await store.pref('whatIfScores', true)) !== false; // Settings → Grades → Show what-if scores
    if (!whatIfAllowed) st.on = false;
    let focusId = null;

    function draw() {
      const gm = store.gradeModel(groups, c, st.values, st.on, dark);
      const parts = [];
      if (st.on) {
        parts.push(U.el('bcv-banner', [
          U.svg(IC.warn, { size: 24, stroke: 'var(--bcv-red)', width: 2, style: { flex: 'none' } }),
          h('div', { style: { flex: '1', minWidth: '200px' } }, [U.text('bcv-banner__title', 'This is not your actual score.'), U.text('bcv-banner__sub', 'What-if mode — your real scores are loaded in; edit any of them to test outcomes. Nothing is saved or sent to your instructor.')]),
          U.btn('Clear all what-if scores', { kind: 'danger', onClick: () => { st.values = {}; draw(); } }),
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
      for (const r of gm.rings) svg.append(circle(r.r, r.track, null, null, r.w));
      for (const r of gm.rings) svg.append(circle(r.r, r.arc, r.cap, r.dash, r.w));
      const center = U.el('bcv-gr__center', [
        U.text('bcv-gr__label', gm.center.label),
        h('span', { class: 'bcv-gr__total', style: { color: gm.center.color }, text: gm.center.value }),
        U.text('bcv-gr__note bcv-pretty', gm.center.note),
        whatIfAllowed ? h('button', { type: 'button', class: `bcv-whatif-btn ${st.on ? 'is-on' : ''}`, text: st.on ? 'Exit what-if mode' : 'Try what-if scores', onclick: () => { st.on = !st.on; draw(); } }) : null,
      ]);

      // ---- by group ------------------------------------------------------------------
      const legendRow = (r) => U.el('bcv-legend__row', [
        h('span', { class: 'bcv-legend__dot', style: r.zero
          ? { background: `radial-gradient(${r.color} 34%, transparent 36%) 0 0 / 2.4px 2.4px` }
          : (r.ringed ? { background: r.color } : { background: 'transparent', border: `1.5px solid ${r.color}` }) }),
        U.el('bcv-legend__body', [U.text('bcv-legend__label', r.label, 'span'), U.text('bcv-legend__detail', r.detail, 'span')]),
        U.text('bcv-legend__weight', r.weightText, 'span'),
        U.text(`bcv-legend__value ${r.zero ? 'bcv-legend__value--muted' : ''}`, r.value, 'span'),
      ]);
      const ungradedRow = (u) => U.el('bcv-legend__row bcv-legend__row--ungraded', [
        h('span', { class: 'bcv-legend__dot' }),
        U.el('bcv-legend__body', [U.text('bcv-legend__label bcv-ungraded__name', u.name, 'span'), U.text('bcv-legend__detail', 'Nothing graded yet — no ring', 'span')]),
        U.text('bcv-legend__weight', u.weightText, 'span'),
        U.text('bcv-legend__value', '—', 'span'),
      ]);
      const byGroup = U.el('bcv-gr__sec', [
        U.text('bcv-gr__h', 'By group'),
        ...gm.legend.map(legendRow),
        ...gm.ungraded.map(ungradedRow),
        gm.legend.length || gm.ungraded.length ? null : U.text('bcv-gr__note', 'No assignment groups in this course.'),
      ]);

      // ---- how the grade is weighted ------------------------------------------------
      let weightsSec;
      if (gm.weighted) {
        weightsSec = U.el('bcv-gr__sec bcv-gr__sec--w', [
          U.el('bcv-gr__hrow', [U.text('bcv-gr__h', 'How the grade is weighted', 'span'), U.text('bcv-gr__hsub', `${gm.weightSum}% of final grade`, 'span')]),
          gm.weightBar.length ? U.el('bcv-wbar', gm.weightBar.map((w) => h('div', { class: `bcv-wbar__seg ${w.graded ? '' : 'bcv-wbar__seg--ungraded'}`, style: { flex: `${w.weight} 1 0`, background: w.color || '' }, title: `${w.name} · ${w.weight}%` }))) : null,
          U.el('bcv-wbar__rows', gm.weightBar.map((w) => U.el('bcv-wbar__row', [
            h('span', { class: `bcv-wbar__dot ${w.graded ? '' : 'bcv-wbar__dot--ungraded'}`, style: { background: w.color || '' } }),
            U.text('bcv-wbar__name', w.name, 'span'),
            U.text('bcv-wbar__w', `${w.weight}% of grade`, 'span'),
            U.text(`bcv-wbar__score ${w.graded ? '' : 'bcv-wbar__score--ungraded'}`, w.scoreLabel, 'span'),
          ]))),
          gm.weightBar.length ? null : U.text('bcv-gr__wnote bcv-pretty', 'No assignment group carries weight yet.'),
          gm.weightNote ? h('p', { class: 'bcv-gr__wnote bcv-pretty', text: gm.weightNote }) : null,
        ]);
      } else {
        weightsSec = U.el('bcv-gr__sec', [
          U.text('bcv-gr__h', 'How the grade is weighted'),
          h('p', { class: 'bcv-gr__wnote bcv-pretty', text: 'This course does not weight assignment groups: the total is points earned over points possible.' }),
        ]);
      }
      const ringsCard = U.card(U.el('bcv-gr', [U.el('bcv-gr__top', [svg, center]), byGroup, weightsSec]), 'bcv-card--22');

      // ---- assignments ----------------------------------------------------------------
      const rows = gm.rows.map((g) => {
        const dotEl = h('span', { class: 'bcv-dot bcv-dot--7', style: { background: g.earned !== null ? '#0a84ff' : 'transparent' } });
        let scoreEl;
        if (!st.on) {
          scoreEl = h('span', { class: 'bcv-grade__score', title: 'Double-click to test a what-if score', text: `${g.grade ? `${g.grade} · ` : ''}${g.earned === null ? '—' : store.fmtPts(g.earned)} / ${store.fmtPts(g.possible)}` }); // (a letter or pass/fail grade first, as Canvas names it)
          // the row opens the assignment, so the score has to keep its own presses: otherwise the
          // first half of the double-click that starts a what-if would navigate away instead
          scoreEl.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); });
          scoreEl.addEventListener('dblclick', () => { st.on = true; focusId = g.id; draw(); });
        } else {
          const input = h('input', { type: 'text', inputmode: 'decimal', placeholder: '—', class: `bcv-whatif__input ${g.hypothetical ? 'is-hyp' : ''}`, dataset: { wf: g.id }, value: st.values[g.id] === undefined ? (g.earned === null ? '' : String(g.earned)) : st.values[g.id] });
          input.addEventListener('focus', () => input.select());
          input.addEventListener('change', () => { st.values[g.id] = input.value.replace(/[^0-9.]/g, ''); focusId = null; draw(); });
          input.addEventListener('keydown', (e) => { if (e.key === 'Enter') input.blur(); });
          scoreEl = U.el('bcv-whatif', [input, U.text('bcv-whatif__possible', `/ ${store.fmtPts(g.possible)}`, 'span')]);
          scoreEl.addEventListener('click', (e) => { e.preventDefault(); e.stopPropagation(); }); // typing a what-if is not opening the row
        }
        // the whole row opens the assignment it is a line about — a grade is the start of a question
        // ("why?"), and the answer is on the assignment's own page
        return U.row([
          U.el('bcv-row__body', [
            h('div', { style: { display: 'flex', alignItems: 'baseline', gap: '8px', flexWrap: 'wrap' } }, [U.text('bcv-grade__name bcv-pretty', g.name, 'span'), g.badge ? U.badge(g.badge, g.badge === 'Late' ? 'orange' : g.badge === 'Missing' ? 'red' : '', 'bcv-badge--xs') : null]),
            // the group, the dates, and — on a marked assignment — how the class did (Canvas's own mean, high and low)
            U.text('bcv-row__sub', [g.group, g.due ? `due ${U.fmtBy(g.due)}` : 'no due date', g.submitted ? `submitted ${U.fmtAt(g.submitted)}` : (g.due && U.parse(g.due) > new Date() ? 'not due yet' : 'not submitted'), g.stats && g.earned !== null ? `class mean ${store.fmtPts(g.stats.mean)} · high ${store.fmtPts(g.stats.max)} · low ${store.fmtPts(g.stats.min)}` : null].filter(Boolean).join(' · ')),
          ]),
          dotEl,
          scoreEl,
          g.url ? U.chev() : null,
        ], { href: g.url || null });
      });
      const tableCard = h('div', {}, [
        U.el('bcv-group__head bcv-group__head--10', [U.h2('Assignments'), U.text('bcv-group__sub bcv-ml-auto', 'Arranged by due date', 'span')]),
        rows.length ? U.card(rows, 'bcv-card--list') : U.emptyCard('No assignments in this course.'),
      ]);

      // The weights live in the grade card's "How the grade is weighted" section; no separate card.
      b.replaceChildren(...parts, U.el('bcv-grades__main', [ringsCard, tableCard]));
      if (focusId) {
        const el = b.querySelector(`[data-wf="${CSS.escape(focusId)}"]`);
        if (el) { el.focus(); el.select(); }
        focusId = null;
      }
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
    return b;
  }

  BCV.screens.grades = { render };
})();
