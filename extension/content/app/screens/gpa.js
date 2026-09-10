/* Grades (sidebar): every current course's Canvas score on one page, a term
 * GPA on the plain 4.0 scale (every course counts equally — Canvas publishes
 * no credit weighting), an optional cumulative GPA and a daily history the
 * page keeps itself once tracking is on, target grades with the arithmetic
 * behind them, and a few honest stats. None of it is the registrar's GPA,
 * and the page says where every number came from. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  const SCALE = [['A', 93, 4], ['A−', 90, 3.7], ['B+', 87, 3.3], ['B', 83, 3], ['B−', 80, 2.7], ['C+', 77, 2.3], ['C', 73, 2], ['C−', 70, 1.7], ['D', 60, 1], ['F', 0, 0]];
  const POINTS = { 'A+': 4, A: 4, 'A-': 3.7, 'B+': 3.3, B: 3, 'B-': 2.7, 'C+': 2.3, C: 2, 'C-': 1.7, 'D+': 1.3, D: 1, 'D-': 0.7, F: 0 };
  const norm = (g) => String(g || '').replace(/−/g, '-').toUpperCase().trim();
  const letterFor = (pct) => SCALE.find((s) => pct >= s[1]) || SCALE[SCALE.length - 1];
  // Canvas's own letter when the course publishes one, else the standard scale
  const pointsFor = (letter, pct) => (norm(letter) in POINTS ? POINTS[norm(letter)] : letterFor(pct)[2]);
  const gpa2 = (n) => (n === null || n === undefined ? '—' : n.toFixed(2));
  const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
  const shortCode = (c) => (c.shortName || c.name).replace(/^[A-Z]\d{2}-/, '');
  const dayKey = (d = new Date()) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  const GEAR = 'M12 15a3 3 0 100-6 3 3 0 000 6zM19.4 15a1.7 1.7 0 00.3 1.9l.1.1a2 2 0 11-2.8 2.8l-.1-.1a1.7 1.7 0 00-2.9 1.2v.2a2 2 0 11-4 0v-.1a1.7 1.7 0 00-2.9-1.3l-.1.1a2 2 0 11-2.8-2.8l.1-.1a1.7 1.7 0 00-1.2-2.9H3a2 2 0 110-4h.2a1.7 1.7 0 001.2-2.9l-.1-.1a2 2 0 112.8-2.8l.1.1a1.7 1.7 0 002.9-1.2V3a2 2 0 114 0v.2a1.7 1.7 0 002.9 1.2l.1-.1a2 2 0 112.8 2.8l-.1.1a1.7 1.7 0 001.2 2.9H21a2 2 0 110 4h-.2a1.7 1.7 0 00-1.4 1z';
  const TREND = 'M4 17l5-6 4 3 6-8';
  const BARS = 'M4 19h16M7 16V9M12 16V5M17 16v-4';
  const MIN_Y = 2.4, MAX_Y = 4.0;
  const LOWER_BY = 10; // the "if ungraded work lands lower" what-if: every remaining score 10 points under today's

  /** Points earned and still to come in a course, and what the remaining work
   *  must average to land a target. Counts only work that moves the final
   *  grade (no 0-point or "not counted" items), and follows the course's
   *  group weights when it uses them. */
  function courseMath(groupsRaw, weighted) {
    const isGraded = (a) => a.submission?.workflow_state === 'graded' && a.submission.score !== null && a.submission.score !== undefined;
    const groups = (groupsRaw || []).map((g) => {
      const items = (g.assignments || []).filter((a) => a.published !== false && !a.omit_from_final_grade && Number(a.points_possible) > 0 && !a.submission?.excused);
      const tot = items.reduce((s, a) => s + Number(a.points_possible), 0);
      const earned = items.filter(isGraded).reduce((s, a) => s + Number(a.submission.score), 0);
      const rem = items.filter((a) => !isGraded(a)).reduce((s, a) => s + Number(a.points_possible), 0);
      return { weight: Number(g.group_weight) || 0, tot, earned, rem };
    }).filter((g) => g.tot > 0);
    const share = (g) => (weighted ? g.weight : g.tot);
    const W = groups.reduce((s, g) => s + share(g), 0);
    // final = base + slope × (fraction scored on the remaining work)
    const base = W > 0 ? groups.reduce((s, g) => s + share(g) * (g.earned / g.tot), 0) / W : 0;
    const slope = W > 0 ? groups.reduce((s, g) => s + share(g) * (g.rem / g.tot), 0) / W : 0;
    return {
      known: groups.length > 0,
      earned: groups.reduce((s, g) => s + g.earned, 0),
      remaining: groups.reduce((s, g) => s + g.rem, 0),
      needed: (targetPct) => (slope > 0 ? ((targetPct / 100 - base) / slope) * 100 : null), // % of the remaining points
    };
  }

  async function render(ctx) {
    const screen = U.el('bcv-screen', null, { style: { '--w': '900px' } });
    const sub = U.text('bcv-head__sub', 'Loading…');
    const body = U.el('bcv-body bcv-body--16');
    screen.append(U.el('bcv-head', U.el('bcv-head__in', U.el('bcv-head__row', h('div', {}, [h('h1', { class: 'bcv-h1', text: 'Grades' }), sub])))), body);
    body.append(U.loading());

    const [all, term, trackingPref, goalPref, targetsPref, snapsPref] = await Promise.all([
      store.courses().catch(() => null), store.currentTerm().catch(() => ''),
      store.pref('gpaTracking'), store.pref('gpaGoal'), store.pref('gradeTargets'), store.pref('gpaSnapshots'),
    ]);
    if (!ctx.alive()) return screen;
    if (!all) {
      body.replaceChildren(U.errorBox('Your courses could not be loaded.'));
      return screen;
    }
    const courses = all.filter((c) => c.state === 'current');
    const groupsBy = new Map();
    await Promise.all(courses.map(async (c) => groupsBy.set(c.id, await store.assignmentGroups(c.id).catch(() => null))));
    if (!ctx.alive()) return screen;

    let tracking = trackingPref && typeof trackingPref === 'object' && Number.isFinite(trackingPref.priorGpa) ? trackingPref : null;
    let goal = Number.isFinite(goalPref) ? goalPref : 3.7;
    const targets = targetsPref && typeof targetsPref === 'object' ? { ...targetsPref } : {};
    let snaps = Array.isArray(snapsPref) ? snapsPref : [];
    let infoOpen = null;

    // ---- the model: every number from a Canvas field or from user input ----------------
    function model() {
      const scored = courses.filter((c) => c.score !== null && c.score !== undefined);
      const unscored = courses.filter((c) => c.score === null || c.score === undefined);
      const rows = scored.map((c) => {
        const pct = Number(c.score);
        const letter = c.grade ? String(c.grade).replace(/-/g, '−') : letterFor(pct)[0];
        const pts = pointsFor(c.grade, pct);
        const m = courseMath(groupsBy.get(c.id), c.weighted);
        const found = SCALE.findIndex((s) => norm(s[0]) === norm(letter));
        const defaultIdx = found >= 0 ? found : SCALE.indexOf(letterFor(pct));
        const idx = clamp(Number.isInteger(targets[c.id]) ? targets[c.id] : defaultIdx, 0, SCALE.length - 1);
        const target = SCALE[idx];
        const needed = m.known ? m.needed(target[1]) : null;
        const met = needed === null ? pct >= target[1] : needed <= 0;
        const reachable = needed === null ? met : needed <= 100;
        return { c, pct, letter, pts, m, idx, target, needed, met, reachable };
      });
      const n = rows.length;
      const termGpa = n ? rows.reduce((s, r) => s + r.pts, 0) / n : null;
      const lowGpa = n ? rows.reduce((s, r) => s + pointsFor(null, Math.max(0, r.pct - LOWER_BY)), 0) / n : null;
      const cum = tracking && termGpa !== null ? (tracking.priorGpa * tracking.priorCourses + termGpa * n) / (tracking.priorCourses + n) : null;
      // on-time: every submitted, dated assignment across these courses; Canvas's own `late` flag decides
      let submitted = 0, onTime = 0;
      for (const c of courses) for (const g of groupsBy.get(c.id) || []) for (const a of g.assignments || []) {
        const s = a.submission;
        if (!s?.submitted_at || !a.due_at) continue;
        submitted++;
        if (!s.late) onTime++;
      }
      const today = dayKey();
      const prev = [...snaps].reverse().find((s) => s.date !== today) || null;
      return { rows, unscored, n, termGpa, lowGpa, cum, submitted, onTime, prev };
    }

    /** One snapshot a day while tracking is on; today's is kept current. */
    async function snapshot(m) {
      if (!tracking || m.termGpa === null) return;
      const today = dayKey();
      const cur = snaps.find((s) => s.date === today);
      const scores = Object.fromEntries(m.rows.map((r) => [r.c.id, r.pct]));
      if (cur) {
        if (cur.gpa === m.termGpa) return;
        Object.assign(cur, { gpa: m.termGpa, scores });
      } else snaps.push({ date: today, gpa: m.termGpa, scores });
      snaps = snaps.slice(-400);
      await store.setPref('gpaSnapshots', snaps);
    }

    const save = () => Promise.all([store.setPref('gpaTracking', tracking), store.setPref('gpaGoal', goal), store.setPref('gradeTargets', targets)]).catch(() => {});

    // ---- pieces ----------------------------------------------------------------------------
    function hero(m) {
      const goalMet = m.termGpa !== null && m.termGpa >= goal;
      const gap = m.termGpa === null ? null : Math.abs(m.termGpa - goal);
      return U.el('bcv-gpa__hero', [
        U.el('bcv-gpa__hero-head', [
          U.text('bcv-gpa__kicker', `Term GPA${term ? ` · ${term}` : ''}`, 'span'),
          h('button', { type: 'button', class: 'bcv-gpa__gear', title: 'GPA settings', 'aria-label': 'GPA settings', onclick: () => openSettings(m) }, U.svg(GEAR, { size: 15, stroke: '#fff', width: 1.9 })),
        ]),
        U.el('bcv-gpa__big', [U.text('bcv-gpa__value', gpa2(m.termGpa), 'span'), U.text('bcv-gpa__of', 'of 4.00', 'span')]),
        U.text('bcv-gpa__hero-note', m.n ? `${U.plural(m.n, 'course')} this term · computed from your Canvas scores` : 'No course has a Canvas score yet'),
        U.el('bcv-gpa__hero-foot', [
          tracking
            ? h('div', {}, [
              U.el('bcv-gpa__line', [U.text('bcv-gpa__line-k', 'Cumulative', 'span'), U.text('bcv-gpa__line-v', gpa2(m.cum), 'span')]),
              U.text('bcv-gpa__hero-sub', `${gpa2(tracking.priorGpa)} across ${U.plural(tracking.priorCourses, 'course')} before this term`),
            ])
            : U.text('bcv-gpa__hero-hint bcv-pretty', 'Cumulative GPA needs your past record — turn on tracking above.'),
          U.el('bcv-gpa__line', [U.text('bcv-gpa__line-k bcv-gpa__line-k--sm', `If ungraded work lands ${LOWER_BY} pts lower`, 'span'), U.text('bcv-gpa__line-v bcv-gpa__line-v--sm', m.termGpa === null ? '—' : `${gpa2(m.lowGpa)} – ${gpa2(m.termGpa)}`, 'span')]),
          U.el('bcv-gpa__goal', [
            U.svg(goalMet ? 'M5 13l4 4L19 7' : 'M12 5v14M6 13l6 6 6-6', { size: 16, stroke: '#fff', width: 2.1, style: { flex: 'none' } }),
            h('div', { style: { flex: '1', minWidth: '0' } }, [
              U.text('bcv-gpa__goal-h', gap === null ? 'No score to compare yet' : goalMet ? `+${gpa2(gap)} above your goal` : `${gpa2(gap)} below your goal`),
              U.text('bcv-gpa__goal-s', `Goal ${gpa2(goal)} · set it in settings`),
            ]),
          ]),
        ]),
      ]);
    }

    function trend() {
      const pts = snaps.slice(-8);
      const enough = tracking && pts.length >= 2;
      const yAt = (v) => 92 - ((clamp(v, MIN_Y, MAX_Y) - MIN_Y) / (MAX_Y - MIN_Y)) * 84;
      let chart = null;
      if (enough) {
        const coords = pts.map((s, i) => ({ x: 7 + i * (86 / (pts.length - 1)), y: yAt(s.gpa), s }));
        const ns = 'http://www.w3.org/2000/svg';
        const svg = document.createElementNS(ns, 'svg');
        svg.setAttribute('viewBox', '0 0 100 100');
        svg.setAttribute('preserveAspectRatio', 'none');
        const goalLine = document.createElementNS(ns, 'line');
        for (const [k, v] of Object.entries({ x1: '0', y1: yAt(goal).toFixed(2), x2: '100', y2: yAt(goal).toFixed(2), stroke: '#5856d6', 'stroke-width': '1.5', 'stroke-dasharray': '4 4', 'vector-effect': 'non-scaling-stroke', opacity: '.8' })) goalLine.setAttribute(k, v);
        const line = document.createElementNS(ns, 'polyline');
        for (const [k, v] of Object.entries({ points: coords.map((p) => `${p.x.toFixed(2)},${p.y.toFixed(2)}`).join(' '), fill: 'none', stroke: '#0a84ff', 'stroke-width': '3', 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'vector-effect': 'non-scaling-stroke' })) line.setAttribute(k, v);
        svg.append(goalLine, line);
        const label = (s) => (s.date === dayKey() ? 'Today' : U.fmtShort(`${s.date}T12:00:00`));
        chart = [
          U.el('bcv-gpa__chart', [svg, ...coords.map((p) => h('span', { class: 'bcv-gpa__pt', style: { left: `${p.x.toFixed(2)}%`, top: `${p.y.toFixed(2)}%` } }))]),
          U.el('bcv-gpa__axis', coords.map((p) => h('span', { class: 'bcv-gpa__tick', style: { left: `${p.x.toFixed(2)}%` } }, [U.text('bcv-gpa__tick-v', gpa2(p.s.gpa), 'span'), U.text('bcv-gpa__tick-l', label(p.s), 'span')]))),
          h('p', { class: 'bcv-gpa__note bcv-pretty', text: `${U.plural(snaps.length, 'snapshot')} since ${U.fmtShort(`${snaps[0].date}T12:00:00`)} — Canvas keeps no grade history, so tracking starts the day you turn it on.` }),
        ];
      } else {
        chart = [U.el('bcv-gpa__empty', [
          U.svg(TREND, { size: 22, stroke: 'var(--bcv-ink3)', width: 1.8 }),
          h('span', { class: 'bcv-pretty', text: !tracking ? 'No history yet. Tracking records one snapshot a day from the moment you turn it on — nothing can be back-filled.' : 'One snapshot so far. The line appears with the second, on the next day you open this page.' }),
        ])];
      }
      return U.el('bcv-gpa__trend', [
        U.el('bcv-gpa__trend-head', [U.text('bcv-gpa__stat-label', 'Trend', 'span'), U.text('bcv-gpa__trend-range', `${gpa2(MIN_Y)} – ${gpa2(MAX_Y)} · dashed line is your goal`, 'span')]),
        ...chart,
      ]);
    }

    function stats(m) {
      const momentum = tracking && m.prev && m.termGpa !== null ? m.termGpa - m.prev.gpa : null;
      const best = [...m.rows].sort((a, b) => b.pct - a.pct)[0];
      const worst = [...m.rows].sort((a, b) => a.pct - b.pct)[0];
      const card = (key, label, icon, color, value, note, extra = null, info = null) => U.el('bcv-gpa__stat', [
        U.el('bcv-gpa__stat-head', [
          U.svg(icon, { size: 14, stroke: color, width: 1.9, style: { flex: 'none' } }),
          U.text('bcv-gpa__stat-label', label, 'span'),
          info ? h('button', { type: 'button', class: 'bcv-gpa__info', title: info, 'aria-label': `About ${label}`, text: 'i', onclick: () => { infoOpen = infoOpen === key ? null : key; draw(); } }) : null,
        ]),
        info && infoOpen === key ? U.el('bcv-gpa__pop', [h('div', { class: 'bcv-pretty', text: info }), h('button', { type: 'button', text: 'Got it', onclick: () => { infoOpen = null; draw(); } })]) : null,
        extra,
        value !== null ? U.text('bcv-gpa__stat-value', value, 'span') : null,
        U.text('bcv-gpa__stat-note bcv-pretty', note, 'span'),
      ]);
      return U.el('bcv-gpa__stats', [
        card('momentum', 'Momentum', IC.chart, momentum !== null && momentum < 0 ? '#ff453a' : '#34c759',
          momentum === null ? '—' : `${momentum >= 0 ? '+' : ''}${gpa2(momentum)}`,
          !tracking ? 'Turn on tracking to compare snapshots' : !m.prev ? 'Needs a second snapshot, on another day' : `Change since the ${U.fmtShort(`${m.prev.date}T12:00:00`)} snapshot`,
          null, 'Momentum is today’s term GPA minus the GPA in the last daily snapshot this page saved. Positive means your scores moved up since then. It needs at least two snapshots, and it only changes when a score in Canvas changes.'),
        card('ontime', 'On-time submissions', IC.clock, '#0a84ff', m.submitted ? `${Math.round((m.onTime / m.submitted) * 100)}%` : '—', m.submitted ? `${m.onTime} of ${m.submitted} submitted before the due time` : 'Nothing submitted yet'),
        card('mix', 'Grade mix', IC.check, '#5856d6', null,
          best ? `Highest ${shortCode(best.c)} at ${best.pct}% · lowest ${shortCode(worst.c)} at ${worst.pct}%` : 'No scores yet',
          m.rows.length ? U.el('bcv-gpa__chips', [...m.rows].sort((a, b) => b.pct - a.pct).map((r) => h('span', { class: 'bcv-gpa__chip', title: `${shortCode(r.c)} · ${r.pct}%` }, [
            h('span', { class: 'bcv-gpa__chip-l', style: { background: r.c.palette.tint, color: r.c.palette.text }, text: r.letter }),
            U.text('bcv-gpa__chip-p', `${r.pct}%`, 'span'),
          ]))) : null),
      ]);
    }

    function courseList(m) {
      const rowFor = (r) => {
        const need = !r.m.known ? 'Target maths needs the course’s assignment list'
          : r.met ? 'Target already secured'
            : r.needed === null ? 'Not reachable — nothing left to grade'
              : r.reachable ? `Needs ${Math.max(0, Math.round(r.needed))}% of the remaining ${store.fmtPts(r.m.remaining)} pts`
                : `Not reachable — would need ${Math.round(r.needed)}%`;
        const bump = (delta) => { targets[r.c.id] = clamp(r.idx + delta, 0, SCALE.length - 1); save(); draw(); };
        return U.el('bcv-gpa__row', [
          U.dot(r.c.color, 'bcv-dot--10'),
          U.el('bcv-gpa__row-body', [
            U.text('bcv-gpa__row-code', r.c.shortName || r.c.name),
            U.text('bcv-gpa__row-sub', `${r.c.nickname ? r.c.originalName : (r.c.code || r.c.name)} · ${r.m.known ? `${store.fmtPts(r.m.earned)} pts earned so far · ${store.fmtPts(r.m.remaining)} pts still to come` : 'score as Canvas reports it'}`),
            U.el('bcv-gpa__row-bar', h('div', { class: 'bcv-gpa__row-fill', style: { width: `${clamp(r.pct, 0, 100)}%`, background: r.c.color } })),
            U.text(`bcv-gpa__need bcv-pretty ${r.met ? 'bcv-gpa__need--met' : !r.reachable ? 'bcv-gpa__need--no' : ''}`, need),
          ]),
          U.el('bcv-gpa__row-score', [U.text('bcv-gpa__row-pct', `${store.fmtPts(r.pct)}%`), U.text('bcv-gpa__row-letter', `${r.letter} · ${r.pts.toFixed(1)}`)]),
          U.el('bcv-gpa__target', [
            h('button', { type: 'button', class: 'bcv-gpa__step', text: '−', title: 'Lower the target', 'aria-label': 'Lower the target', disabled: r.idx >= SCALE.length - 1 || null, onclick: () => bump(1) }),
            U.text('bcv-gpa__target-pill', `${r.target[0]} · ${r.target[1]}%`, 'span'),
            h('button', { type: 'button', class: 'bcv-gpa__step', text: '+', title: 'Raise the target', 'aria-label': 'Raise the target', disabled: r.idx <= 0 || null, onclick: () => bump(-1) }),
          ]),
        ]);
      };
      const unscoredRow = (c) => U.el('bcv-gpa__row bcv-gpa__row--unscored', [
        U.dot(c.color, 'bcv-dot--10'),
        U.el('bcv-gpa__row-body', [U.text('bcv-gpa__row-code', c.shortName || c.name), U.text('bcv-gpa__row-sub', 'No score yet · Canvas has not computed one, so it is left out of the GPA')]),
        U.el('bcv-gpa__row-score', [U.text('bcv-gpa__row-pct', '—'), U.text('bcv-gpa__row-letter', 'no score')]),
      ]);
      return h('div', {}, [
        U.el('bcv-group__head', [U.h2('All courses'), U.text('bcv-group__sub bcv-ml-auto', 'Tap −/+ to set a target grade', 'span')]),
        m.rows.length || m.unscored.length ? U.card([...m.rows.map(rowFor), ...m.unscored.map(unscoredRow)], 'bcv-card--list') : U.emptyCard('No current courses.'),
      ]);
    }

    // ---- settings sheet ----------------------------------------------------------------------
    function openSettings(m, { wantTracking = false } = {}) {
      document.querySelector('.bcv-sheet-ov')?.remove();
      const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': 'GPA settings' });
      let pendingGoal = goal;
      let on = !!tracking || wantTracking;
      const priorGpa = h('input', { class: 'bcv-input bcv-gpa-set__input', id: 'bcv-gpa-prior', type: 'number', min: '0', max: '4', step: '0.01', placeholder: '3.42', value: tracking ? String(tracking.priorGpa) : '' });
      const priorN = h('input', { class: 'bcv-input bcv-gpa-set__input', id: 'bcv-gpa-prior-n', type: 'number', min: '1', max: '200', step: '1', placeholder: '8', value: tracking ? String(tracking.priorCourses) : '' });
      const goalVal = U.text('bcv-gpa-set__val', gpa2(pendingGoal), 'span');
      const goalGap = U.text('bcv-gpa-set__s bcv-pretty', '');
      const syncGoal = () => {
        goalVal.textContent = gpa2(pendingGoal);
        goalGap.textContent = m.termGpa === null ? 'Set the term GPA you are aiming for' : m.termGpa >= pendingGoal ? `You are ${gpa2(m.termGpa - pendingGoal)} above this goal` : `You are ${gpa2(pendingGoal - m.termGpa)} below this goal`;
      };
      syncGoal();
      const trackBody = U.el('bcv-gpa-set__fields', [
        h('label', { class: 'bcv-gpa-set__field' }, [h('span', { text: 'GPA before this term' }), priorGpa]),
        h('label', { class: 'bcv-gpa-set__field' }, [h('span', { text: 'Courses it covers' }), priorN]),
      ]);
      trackBody.hidden = !on;
      const trackSwitch = U.switchEl(on, (v) => { on = v; trackBody.hidden = !on; }, 'Track GPA over time');
      const close = () => ov.remove();
      const done = async () => {
        goal = clamp(pendingGoal, 0, 4);
        if (on) {
          const g = Number(priorGpa.value), n = Math.round(Number(priorN.value));
          if (!Number.isFinite(g) || g < 0 || g > 4 || !(n >= 1)) {
            U.toast('Enter your GPA before this term (0–4) and how many courses it covers.', { error: true });
            priorGpa.focus();
            return;
          }
          tracking = { priorGpa: g, priorCourses: n, since: tracking?.since || dayKey() };
        } else tracking = null;
        await save();
        close();
        await refresh();
      };
      ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
      ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
      ov.append(U.el('bcv-sheet bcv-gpa-set', [
        U.el('bcv-sheet__head', [
          h('div', { style: { flex: '1', minWidth: '0' } }, [U.text('bcv-sheet__title', 'GPA settings'), U.text('bcv-sheet__desc bcv-pretty', 'Term GPA is the plain average of your course letter grades on a 4.0 scale — Canvas publishes no credit weighting, so nothing here is guessed.')]),
          h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
        ]),
        U.el('bcv-sheet__list', [
          U.el('bcv-gpa-set__sec bcv-gpa-set__sec--tint', [
            U.el('bcv-gpa-set__body', [U.text('bcv-gpa-set__k', 'Term GPA goal'), goalGap]),
            U.el('bcv-gpa-set__ctl', [
              h('button', { type: 'button', class: 'bcv-gpa-set__step', text: '−', 'aria-label': 'Lower the goal', onclick: () => { pendingGoal = clamp(+(pendingGoal - 0.05).toFixed(2), 0, 4); syncGoal(); } }),
              goalVal,
              h('button', { type: 'button', class: 'bcv-gpa-set__step', text: '+', 'aria-label': 'Raise the goal', onclick: () => { pendingGoal = clamp(+(pendingGoal + 0.05).toFixed(2), 0, 4); syncGoal(); } }),
            ]),
          ]),
          U.el('bcv-gpa-set__sec', [
            U.el('bcv-gpa-set__body', [U.text('bcv-gpa-set__k', 'Track GPA over time'), U.text('bcv-gpa-set__s bcv-pretty', 'Canvas stores no GPA and no history. With your record before this term, this page shows a cumulative GPA and keeps one snapshot a day from now on.')]),
            trackSwitch,
            trackBody,
          ]),
        ]),
        U.el('bcv-gpa-set__foot', [
          h('span', { text: m.n ? `${U.plural(m.n, 'course')} this term · every course counts equally` : 'No scored courses yet' }),
          U.btn('Done', { kind: 'primary', cls: 'bcv-btn--fill36', onClick: done }),
        ]),
      ]));
      document.body.append(ov);
      ov.tabIndex = -1;
      if (wantTracking) priorGpa.focus(); else ov.focus();
    }

    async function resetTracking() {
      if (!window.confirm('Forget your prior GPA and every snapshot this page has saved?')) return;
      tracking = null;
      snaps = [];
      await Promise.all([save(), store.setPref('gpaSnapshots', [])]);
      await refresh();
    }

    // ---- draw --------------------------------------------------------------------------------
    let current = null;
    async function refresh() {
      current = model();
      await snapshot(current);
      if (ctx.alive()) draw();
    }
    function draw() {
      const m = current || model();
      sub.textContent = `${term ? `${term} · ` : ''}${m.n ? `${U.plural(m.n, 'course')} this term · every course counts equally` : 'no scored courses yet'}`;
      body.replaceChildren(...[
        tracking ? null : U.el('bcv-gpa__banner', [
          U.svg(BARS, { size: 19, stroke: 'var(--bcv-blue)', width: 1.9, style: { flex: 'none' } }),
          U.el('bcv-gpa__banner-body', [U.text('bcv-gpa__banner-title', 'Track GPA over time?'), U.text('bcv-gpa__banner-sub bcv-pretty', 'Canvas stores no GPA and no history. Add your GPA before this term and how many courses it covers, and this page keeps its own daily record.')]),
          U.btn('Turn on tracking', { kind: 'primary', cls: 'bcv-btn--fill36', onClick: () => openSettings(m, { wantTracking: true }) }),
        ]),
        U.el('bcv-gpa__top', [hero(m), trend()]),
        stats(m),
        courseList(m),
        U.el('bcv-gpa__foot', [
          h('p', { class: 'bcv-pretty', text: tracking
            ? 'Course scores come straight from Canvas. GPA, targets and history are computed here from the standard 4.0 scale and the prior record you entered — your school’s official GPA may differ.'
            : 'Course scores and target maths come straight from Canvas. Term GPA is the plain 4.0-scale average of your courses; cumulative GPA and history stay empty until you turn on tracking.' }),
          tracking ? h('button', { type: 'button', class: 'bcv-gpa__linkbtn', text: 'Reset setup', onclick: resetTracking }) : null,
        ]),
      ].filter(Boolean));
      ctx.setSmart({
        label: 'Grades',
        actions: [
          { label: 'Explain my GPA', note: m.termGpa === null ? 'No scores yet' : `${gpa2(m.termGpa)} this term`, icon: IC.chart, prompt: 'Explain how my term GPA is built from my course scores and letter grades, and which course moves it most.' },
          { label: 'Reach my goal', note: `Goal ${gpa2(goal)}`, icon: IC.bolt, prompt: 'Given each course’s score, target and the points still to come, what do I need in each course to reach my GPA goal? Keep the arithmetic brief.' },
        ],
        context: () => [`Term GPA ${gpa2(m.termGpa)} (goal ${gpa2(goal)}), ${m.n} scored courses, every course weighted equally.`, ...m.rows.map((r) => `- ${r.c.name}: ${r.pct}% (${r.letter}, ${r.pts.toFixed(1)}); target ${r.target[0]} ${r.target[1]}%; ${r.m.known ? `${store.fmtPts(r.m.earned)} pts earned, ${store.fmtPts(r.m.remaining)} pts remaining; needs ${r.needed === null ? 'n/a' : `${Math.round(r.needed)}%`} of the rest` : 'assignment list unavailable'}`), ...m.unscored.map((c) => `- ${c.name}: no score yet`)].join('\n'),
      });
    }

    await refresh();
    return screen;
  }

  BCV.screens.gpa = { render, courseMath, SCALE };
})();
