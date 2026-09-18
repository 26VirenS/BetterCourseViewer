/* Grades (sidebar): every current course's Canvas score on one page, a term
 * GPA on the plain 4.0 scale (every course counts equally — Canvas publishes
 * no credit weighting), an optional cumulative GPA and a daily history the
 * page keeps itself once tracking is on, one card per course whose ring
 * opens into the group breakdown, a Details sheet with the course's own
 * grade page, target grades with the arithmetic behind them, and a few
 * honest stats. None of it is the registrar's GPA, and the page says where
 * every number came from. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;

  const SCALE = [['A+', 97, 4], ['A', 93, 4], ['A−', 90, 3.7], ['B+', 87, 3.3], ['B', 83, 3], ['B−', 80, 2.7], ['C+', 77, 2.3], ['C', 73, 2], ['C−', 70, 1.7], ['D', 60, 1], ['F', 0, 0]];
  const OLD_SCALE = ['A', 'A−', 'B+', 'B', 'B−', 'C+', 'C', 'C−', 'D', 'F']; // targets saved before A+ existed were indices into this
  const POINTS = { 'A+': 4, A: 4, 'A-': 3.7, 'B+': 3.3, B: 3, 'B-': 2.7, 'C+': 2.3, C: 2, 'C-': 1.7, 'D+': 1.3, D: 1, 'D-': 0.7, F: 0 };
  const DEFAULT_GOAL = 4; // the goal the setup starts from, for anyone who skipped it
  const norm = (g) => String(g || '').replace(/−/g, '-').toUpperCase().trim();
  /** A saved target (a letter, or an index into the scale before A+) → its index on SCALE, or -1. */
  const targetIndex = (t) => {
    const letter = Number.isInteger(t) ? OLD_SCALE[t] : typeof t === 'string' ? t : null;
    return letter ? SCALE.findIndex((s) => norm(s[0]) === norm(letter)) : -1;
  };
  const letterFor = (pct) => SCALE.find((s) => pct >= s[1]) || SCALE[SCALE.length - 1];
  const isPassFail = (t) => typeof t === 'string' && /^p\s*\/\s*f$/i.test(t.trim()); // the setup's Pass/Fail switch, saved in place of a letter
  // Canvas's own letter when the course publishes one, else the standard scale
  const pointsFor = (letter, pct) => (norm(letter) in POINTS ? POINTS[norm(letter)] : letterFor(pct)[2]);
  const gpa2 = (n) => (n === null || n === undefined ? '—' : n.toFixed(2));
  const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
  const shortCode = (c) => (c.shortName || c.name).replace(/^[A-Z]\d{2}-/, '');
  const dayKey = (d = new Date()) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  const GEAR = 'M12 15a3 3 0 100-6 3 3 0 000 6zM19.4 15a1.7 1.7 0 00.3 1.9l.1.1a2 2 0 11-2.8 2.8l-.1-.1a1.7 1.7 0 00-2.9 1.2v.2a2 2 0 11-4 0v-.1a1.7 1.7 0 00-2.9-1.3l-.1.1a2 2 0 11-2.8-2.8l.1-.1a1.7 1.7 0 00-1.2-2.9H3a2 2 0 110-4h.2a1.7 1.7 0 001.2-2.9l-.1-.1a2 2 0 112.8-2.8l.1.1a1.7 1.7 0 002.9-1.2V3a2 2 0 114 0v.2a1.7 1.7 0 002.9 1.2l.1-.1a2 2 0 112.8 2.8l-.1.1a1.7 1.7 0 001.2 2.9H21a2 2 0 110 4h-.2a1.7 1.7 0 00-1.4 1z';
  const TREND = 'M4 17l5-6 4 3 6-8';
  const BARS = 'M4 19h16M7 16V9M12 16V5M17 16v-4';
  const EYE_OFF = 'M3 12s3.5-6 9-6 9 6 9 6-3.5 6-9 6-9-6-9-6zM4 4l16 16';
  const MIN_Y = 2.4, MAX_Y = 4.0;
  const LOWER_BY = 10; // the "if ungraded work lands lower" what-if: every remaining score 10 points under today's
  const NS = 'http://www.w3.org/2000/svg';
  const RING_R = 22; // the 56px course ring
  const CAT_R = [16, 10.5, 5]; // nested group rings inside it
  const svgEl = (tag, attrs) => {
    const el = document.createElementNS(NS, tag);
    for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, String(v));
    return el;
  };
  const dashFor = (pct, r) => {
    const c = 2 * Math.PI * r;
    const f = (clamp(pct, 0, 100) / 100) * c;
    return `${f.toFixed(1)} ${(c - f).toFixed(1)}`;
  };

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
    // a dense screen: fills the column up to the 1180px cap (mockup 7's layout notes)
    const screen = U.el('bcv-screen bcv-screen--gpa', null, { style: { '--w': '1180px' } });
    const sub = U.text('bcv-head__sub', 'Loading…');
    const body = U.el('bcv-body bcv-body--16');
    screen.append(U.el('bcv-head', U.el('bcv-head__in', U.el('bcv-head__row', h('div', {}, [h('h1', { class: 'bcv-h1', text: 'Grades' }), sub])))), body);
    body.append(U.loading('cards', 6)); // course-card skeletons: the layout does not jump when the data lands

    const [all, term, trackingPref, goalPref, targetsPref, snapsPref, hiddenPref] = await Promise.all([
      store.courses({ maxAge: store.freshness.grades }).catch(() => null), store.currentTerm().catch(() => ''), // never a score older than the freshness: a tool may have posted one since
      store.pref('gpaTracking'), store.pref('gpaGoal'), store.pref('gradeTargets'), store.pref('gpaSnapshots'), store.pref('gpaHidden'),
    ]);
    if (!ctx.alive()) return screen;
    if (!all) {
      body.replaceChildren(U.errorBox('Your courses could not be loaded.'));
      return screen;
    }
    // the same list every screen follows: the favourites chosen in setup (every current course until one is starred)
    const currentCourses = all.filter((c) => c.state === 'current');
    const starredCourses = currentCourses.filter((c) => c.favorite);
    const courses = starredCourses.length ? starredCourses : currentCourses;
    const groupsBy = new Map();
    await Promise.all(courses.map(async (c) => groupsBy.set(c.id, await store.assignmentGroups(c.id).catch(() => null))));
    if (!ctx.alive()) return screen;

    // tracking is on with or without a record from before this term (the setup asks for none:
    // cumulative GPA fills in as snapshots accumulate); a goal of 0 means no goal was set
    let tracking = trackingPref && typeof trackingPref === 'object' && (trackingPref.since || Number.isFinite(trackingPref.priorGpa))
      ? { ...trackingPref, priorGpa: Number.isFinite(trackingPref.priorGpa) ? trackingPref.priorGpa : null, priorCourses: Number.isFinite(trackingPref.priorCourses) ? trackingPref.priorCourses : 0 }
      : null;
    let goal = Number.isFinite(goalPref) ? goalPref : DEFAULT_GOAL;
    const hasGoal = () => goal > 0;
    const hasPrior = () => !!tracking && Number.isFinite(tracking.priorGpa) && tracking.priorCourses > 0;
    const targets = targetsPref && typeof targetsPref === 'object' ? { ...targetsPref } : {};
    let snaps = Array.isArray(snapsPref) ? snapsPref : [];
    const hidden = new Set(Array.isArray(hiddenPref) ? hiddenPref.map(String) : []); // dropped from the overview and the GPA, reversible
    let infoOpen = null;
    let current = null; // the latest model, shared with the sheets
    let entered = false; // entry motion (mockup 11) plays on the first draw only: GPA rolls, the trend line draws, rings sweep
    const gmCache = new Map();
    /** The course's own grade model (rings, groups, weights, assignment rows), once per course. */
    const gmFor = (c) => {
      if (!gmCache.has(c.id)) gmCache.set(c.id, store.gradeModel(groupsBy.get(c.id) || [], c, {}, false, ctx.dark));
      return gmCache.get(c.id);
    };

    // ---- the model: every number from a Canvas field or from user input ----------------
    function model() {
      const shown = courses.filter((c) => !hidden.has(String(c.id)));
      // a pass/fail course (the setup's switch, saved as P/F in place of a target letter) has no
      // letter to aim at and counts for nothing in the GPA; its score still shows
      const passFail = shown.filter((c) => isPassFail(targets[c.id]));
      const scored = shown.filter((c) => c.score !== null && c.score !== undefined && !isPassFail(targets[c.id]));
      const unscored = shown.filter((c) => (c.score === null || c.score === undefined) && !isPassFail(targets[c.id]));
      const rows = scored.map((c) => {
        const pct = Number(c.score);
        const letter = c.grade ? String(c.grade).replace(/-/g, '−') : letterFor(pct)[0];
        const pts = pointsFor(c.grade, pct);
        const m = courseMath(groupsBy.get(c.id), c.weighted);
        const found = SCALE.findIndex((s) => norm(s[0]) === norm(letter));
        const defaultIdx = found >= 0 ? found : SCALE.indexOf(letterFor(pct));
        const saved = targetIndex(targets[c.id]);
        const idx = clamp(saved >= 0 ? saved : defaultIdx, 0, SCALE.length - 1);
        const target = SCALE[idx];
        const needed = m.known ? m.needed(target[1]) : null;
        const met = needed === null ? pct >= target[1] : needed <= 0;
        const reachable = needed === null ? met : needed <= 100;
        return { c, pct, letter, pts, m, idx, target, needed, met, reachable };
      });
      const n = rows.length;
      const termGpa = n ? rows.reduce((s, r) => s + r.pts, 0) / n : null;
      const lowGpa = n ? rows.reduce((s, r) => s + pointsFor(null, Math.max(0, r.pct - LOWER_BY)), 0) / n : null;
      const cum = hasPrior() && termGpa !== null ? (tracking.priorGpa * tracking.priorCourses + termGpa * n) / (tracking.priorCourses + n) : null;
      // on-time: every submitted, dated assignment across the shown courses; Canvas's own `late` flag decides
      let submitted = 0, onTime = 0;
      for (const c of shown) for (const g of groupsBy.get(c.id) || []) for (const a of g.assignments || []) {
        const s = a.submission;
        if (!s?.submitted_at || !a.due_at) continue;
        submitted++;
        if (!s.late) onTime++;
      }
      const today = dayKey();
      const prev = [...snaps].reverse().find((s) => s.date !== today) || null;
      return { rows, unscored, passFail, n, shownCount: shown.length, hiddenList: courses.filter((c) => hidden.has(String(c.id))), termGpa, lowGpa, cum, submitted, onTime, prev };
    }
    const courseLabel = (m) => `${U.plural(m.shownCount, 'course')} · ${m.n} with grades so far`;
    const needText = (r) => (!r.m.known ? 'Target maths needs the course’s assignment list'
      : r.met ? 'Target already secured'
        : r.needed === null ? 'Not reachable — nothing left to grade'
          : r.reachable ? `Needs ${Math.max(0, Math.round(r.needed))}% of the remaining ${store.fmtPts(r.m.remaining)} pts`
            : `Not reachable — would need ${Math.round(r.needed)}%`);
    const needClass = (r) => (r.met ? 'bcv-gpa__need--met' : !r.reachable ? 'bcv-gpa__need--no' : '');

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
    const saveHidden = () => store.setPref('gpaHidden', [...hidden]);

    // ---- pieces ----------------------------------------------------------------------------
    /** A GPA figure that rolls to its value on entry and is printed as-is on every later draw. */
    const gpaFigure = (cls, value, seed) => {
      const el = U.text(cls, gpa2(value), 'span');
      if (!entered && value !== null && value !== undefined) U.roll(el, value, { decimals: 2, seed });
      return el;
    };
    function hero(m) {
      const goalMet = hasGoal() && m.termGpa !== null && m.termGpa >= goal;
      const gap = !hasGoal() || m.termGpa === null ? null : Math.abs(m.termGpa - goal);
      return U.el('bcv-gpa__hero', [
        U.el('bcv-gpa__hero-head', [
          U.text('bcv-gpa__kicker', `Term GPA${term ? ` · ${term}` : ''}`, 'span'),
          h('button', { type: 'button', class: 'bcv-gpa__gear', title: 'GPA settings', 'aria-label': 'GPA settings', onclick: (e) => openSettings(m, { from: e.currentTarget }) }, U.svg(GEAR, { size: 15, stroke: '#fff', width: 1.9 })),
        ]),
        U.el('bcv-gpa__big', [gpaFigure('bcv-gpa__value', m.termGpa, 0), U.text('bcv-gpa__of', 'of 4.00', 'span')]),
        U.text('bcv-gpa__hero-note', m.shownCount ? `${courseLabel(m)} · computed from your Canvas scores` : 'No current course to score'),
        U.el('bcv-gpa__hero-foot', [
          tracking
            ? h('div', {}, [
              U.el('bcv-gpa__line', [U.text('bcv-gpa__line-k', 'Cumulative', 'span'), gpaFigure('bcv-gpa__line-v', hasPrior() ? m.cum : m.termGpa, 1.3)]),
              U.text('bcv-gpa__hero-sub', hasPrior() ? `${gpa2(tracking.priorGpa)} across ${U.plural(tracking.priorCourses, 'course')} before this term` : 'This term so far; add your record before this term in settings'),
            ])
            : U.text('bcv-gpa__hero-hint bcv-pretty', 'Cumulative GPA needs your past record — turn on tracking above.'),
          U.el('bcv-gpa__line', [U.text('bcv-gpa__line-k bcv-gpa__line-k--sm', `If ungraded work lands ${LOWER_BY} pts lower`, 'span'), U.text('bcv-gpa__line-v bcv-gpa__line-v--sm', m.termGpa === null ? '—' : `${gpa2(m.lowGpa)} – ${gpa2(m.termGpa)}`, 'span')]),
          U.el('bcv-gpa__goal', [
            U.svg(goalMet ? 'M5 13l4 4L19 7' : 'M12 5v14M6 13l6 6 6-6', { size: 16, stroke: '#fff', width: 2.1, style: { flex: 'none' } }),
            h('div', { style: { flex: '1', minWidth: '0' } }, [
              U.text('bcv-gpa__goal-h', !hasGoal() ? 'No goal set' : gap === null ? 'No score to compare yet' : goalMet ? `+${gpa2(gap)} above your goal` : `${gpa2(gap)} below your goal`),
              U.text('bcv-gpa__goal-s', hasGoal() ? `Goal ${gpa2(goal)} · set it in settings` : 'Set one in settings'),
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
        const svg = svgEl('svg', { viewBox: '0 0 100 100', preserveAspectRatio: 'none' });
        if (hasGoal()) svg.append(svgEl('line', { x1: '0', y1: yAt(goal).toFixed(2), x2: '100', y2: yAt(goal).toFixed(2), stroke: '#5856d6', 'stroke-width': '1.5', 'stroke-dasharray': '4 4', 'vector-effect': 'non-scaling-stroke', opacity: '.8' }));
        // on entry the line strokes itself on from left to right (pathLength 420 → one dash the line's length) and the dots follow it
        svg.append(
          svgEl('polyline', { points: coords.map((p) => `${p.x.toFixed(2)},${p.y.toFixed(2)}`).join(' '), fill: 'none', stroke: '#0a84ff', 'stroke-width': '3', 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'vector-effect': 'non-scaling-stroke', pathLength: '420', class: !entered ? 'bcv-gpa__line--draw' : '' }),
        );
        const label = (s) => (s.date === dayKey() ? 'Today' : U.fmtShort(`${s.date}T12:00:00`));
        chart = [
          U.el('bcv-gpa__chart', [svg, ...coords.map((p, i) => h('span', { class: `bcv-gpa__pt ${!entered ? 'bcv-gpa__pt--in' : ''}`, style: { left: `${p.x.toFixed(2)}%`, top: `${p.y.toFixed(2)}%`, '--bcv-delay': `${Math.round(200 + 1050 * (i / Math.max(1, coords.length - 1)))}ms` } }))]),
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
        U.el('bcv-gpa__trend-head', [U.text('bcv-gpa__stat-label', 'Trend', 'span'), U.text('bcv-gpa__trend-range', `${gpa2(MIN_Y)} – ${gpa2(MAX_Y)}${hasGoal() ? ' · dashed line is your goal' : ''}`, 'span')]),
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
      ].map((el, i) => U.enter(el, i, 50)));
    }

    // ---- course cards ------------------------------------------------------------------------
    const ringTrack = () => (ctx.dark ? 'rgba(255,255,255,.1)' : 'rgba(120,120,128,.16)');
    /** One thin ring per graded group (three at most), nested inside the course ring; each
     *  sweeps in as it mounts (on hover, in the Details sheet), 90ms apart. */
    function catCircles(cats) {
      const out = [];
      cats.slice(0, CAT_R.length).forEach((ct, k) => {
        out.push(svgEl('circle', { cx: 28, cy: 28, r: CAT_R[k], fill: 'none', stroke: ringTrack(), 'stroke-width': 4.5 }));
        if (ct.pct !== null) out.push(svgEl('circle', { cx: 28, cy: 28, r: CAT_R[k], fill: 'none', stroke: ct.color, 'stroke-width': 4.5, 'stroke-linecap': 'round', 'stroke-dasharray': dashFor(ct.pct, CAT_R[k]), class: 'bcv-ring--fill bcv-ring--fill-cat', style: `--bcv-delay: ${k * 90}ms` }));
      });
      return out;
    }
    /** The ring (drawn at 82px on the cards, 62px in the Details sheet): the course total, plus the
     *  group rings nested inside when expanded. `delay` (ms) sweeps the course arc in from empty;
     *  null draws it at its value at once. */
    function ringSvg(c, pct, cats, expanded, delay = null) {
      const svg = svgEl('svg', { viewBox: '0 0 56 56', class: 'bcv-gpa__ringsvg' });
      svg.append(svgEl('circle', { cx: 28, cy: 28, r: RING_R, fill: 'none', stroke: ringTrack(), 'stroke-width': 6 }));
      if (pct !== null) svg.append(svgEl('circle', { cx: 28, cy: 28, r: RING_R, fill: 'none', stroke: c.color, 'stroke-width': 6, 'stroke-linecap': 'round', 'stroke-dasharray': dashFor(pct, RING_R), ...(delay === null ? {} : { class: 'bcv-ring--fill', style: `--bcv-delay: ${delay}ms` }) }));
      if (expanded) svg.append(...catCircles(cats));
      return svg;
    }
    const catRow = (ct) => U.el('bcv-gpa__cat', [h('span', { class: 'bcv-gpa__catdot', style: { background: ct.color } }), U.text('bcv-gpa__catname bcv-ellip', ct.label, 'span'), U.text('bcv-gpa__catpct', ct.value, 'span')]);

    /** One card per course. Hovering the ring alone (not the card) opens the group
     *  breakdown in place; the ring column is a fixed box so nothing shifts. The
     *  ring is built once and only its group circles come and go, so the node under
     *  the pointer is never removed and mouseleave always fires. */
    function courseCard(r, i = 0) {
      const c = r.c;
      const ungraded = !!r.ungraded;
      const pf = !!r.passFail;
      const pct = ungraded ? null : r.pct;
      const cats = gmFor(c).legend; // groups with graded work, in Canvas's order (weighted first)
      let hover = false;
      const card = U.el('bcv-gpa__card', null, { dataset: { course: c.id } });
      const svg = ringSvg(c, pct, [], false, entered ? null : Math.min(i * 70, 380)); // rings sweep in 70ms apart down the grid
      const catsG = svgEl('g', { class: 'bcv-gpa__cats' });
      svg.append(catsG);
      const letter = h('span', { class: 'bcv-gpa__ringletter', style: { color: ungraded ? 'var(--bcv-ink3)' : c.palette.text }, text: ungraded ? 'N/A' : pf ? 'P/F' : r.letter });
      const ringWrap = h('div', { class: 'bcv-gpa__ringwrap' }, [svg, letter]);
      const ringBox = h('div', { class: 'bcv-gpa__ringbox', title: cats.length ? 'Hover for the group breakdown' : null }, ringWrap);
      const info = h('div', { class: 'bcv-gpa__cinfo' });
      const hideSlot = h('div', { class: 'bcv-gpa__hideslot' }, h('button', { type: 'button', class: 'bcv-gpa__hide', title: 'Hide this course', 'aria-label': `Hide ${c.shortName || c.name} from the GPA`, onclick: () => hideCourse(c) }, U.svg(EYE_OFF, { size: 14, stroke: 'currentColor', width: 1.9 })));
      const targetChip = ungraded
        ? h('span', { class: 'bcv-gpa__cchip bcv-gpa__cchip--na', text: 'No grade yet' })
        : pf ? h('span', { class: 'bcv-gpa__cchip bcv-gpa__cchip--na', text: 'Pass/Fail' })
          : h('span', { class: 'bcv-gpa__cchip', style: { background: c.palette.tint, color: c.palette.text }, text: `Target ${r.target[0]}` });
      ringBox.addEventListener('mouseenter', () => { if (!hover && cats.length) { hover = true; paint(); } });
      ringBox.addEventListener('mouseleave', () => { if (hover) { hover = false; paint(); } });
      card.append(
        U.el('bcv-gpa__ctop', [ringBox, info, hideSlot]),
        h('div', {}, [
          U.el('bcv-gpa__cbar', [
            h('div', { class: 'bcv-gpa__cfill', style: { width: `${ungraded ? 0 : clamp(pct, 0, 100)}%`, background: c.color } }),
            ungraded || pf ? null : h('span', { class: 'bcv-gpa__tick', title: `Target ${r.target[0]}`, style: { left: `${Math.min(100, r.target[1])}%` } }),
          ]),
          U.text(`bcv-gpa__need bcv-pretty ${ungraded || pf ? '' : needClass(r)}`, ungraded ? 'Nothing graded yet — no score to project from' : pf ? 'Pass/Fail — no letter to aim at' : needText(r)),
          U.text('bcv-gpa__cnote', ungraded ? 'Canvas has not computed a score, so it counts for nothing here' : pf ? 'Counts for nothing in the GPA' : r.m.known ? `${store.fmtPts(r.m.earned)} pts earned so far` : 'Score as Canvas reports it'),
        ]),
        U.el('bcv-gpa__cfoot', [
          targetChip,
          h('button', { type: 'button', class: 'bcv-gpa__details', onclick: (e) => openDetail(c, e.currentTarget) }, ['Details', U.svg(IC.chevron, { size: 13, stroke: 'var(--bcv-blue)', width: 2.1 })]),
        ]),
      );
      let leaving = null; // the timer that clears the group rings once they have swept back out
      function paint() {
        card.classList.toggle('is-hover', hover);
        clearTimeout(leaving);
        leaving = null;
        if (hover) {
          // the ring keeps its size: the group rings nest inside it and sweep in (a sweep-out
          // still under way is simply replaced by a fresh sweep-in)
          letter.hidden = true;
          letter.classList.remove('is-back');
          catsG.replaceChildren(...catCircles(cats));
        } else if (catsG.childElementCount) {
          // the pointer left: the rings sweep back out from wherever they have got to, last in
          // first out, their tracks fading with them; the letter returns once they have gone
          const fills = [...catsG.querySelectorAll('.bcv-ring--fill-cat')];
          const n = fills.length;
          fills.forEach((el, k) => {
            el.style.strokeDasharray = getComputedStyle(el).strokeDasharray;
            el.style.setProperty('--bcv-delay', `${(n - 1 - k) * 60}ms`);
            el.classList.add('bcv-ring--unfill');
          });
          for (const t of catsG.querySelectorAll('circle:not(.bcv-ring--fill-cat)')) t.classList.add('bcv-ring--track-out');
          const total = U.reducedMotion() ? 0 : (n ? (n - 1) * 60 : 0) + 420;
          leaving = setTimeout(() => {
            leaving = null;
            catsG.replaceChildren();
            letter.hidden = false;
            letter.classList.add('is-back');
          }, total);
        } else {
          letter.hidden = false;
        }
        info.replaceChildren(hover
          ? U.el('bcv-gpa__bygroup', [
            U.el('bcv-gpa__bygroup-head', [U.text('bcv-gpa__kicker2', 'By group', 'span'), U.text('bcv-gpa__bygroup-pct', `${store.fmtPts(pct)}%`, 'span')]),
            ...cats.slice(0, 4).map(catRow),
          ])
          : U.el('bcv-gpa__cbody', [
            U.text('bcv-gpa__ccode bcv-ellip', c.shortName || c.name),
            U.text('bcv-gpa__cname bcv-ellip', c.nickname ? c.originalName : (c.code || c.name)),
            U.el('bcv-gpa__cscore', [U.text('bcv-gpa__cpct', ungraded ? 'N/A' : `${store.fmtPts(pct)}%`, 'span'), U.text('bcv-gpa__cpts', ungraded || pf ? '— pts' : `${r.pts.toFixed(1)} pts`, 'span')]),
          ]));
        hideSlot.hidden = hover;
        targetChip.hidden = hover && !ungraded;
      }
      paint();
      return card;
    }
    function courseGrid(m) {
      const cards = [...m.rows, ...m.passFail.map((c) => (c.score === null || c.score === undefined ? { c, ungraded: true, passFail: true } : { c, passFail: true, pct: Number(c.score) })), ...m.unscored.map((c) => ({ c, ungraded: true }))];
      return h('div', {}, [
        U.el('bcv-group__head', [U.h2('All courses'), U.text('bcv-group__sub bcv-ml-auto', 'Hover a ring for the group breakdown', 'span')]),
        cards.length ? U.el('bcv-gpa__grid', cards.map((r, i) => U.enter(courseCard(r, i), i, 55))) : U.emptyCard(m.hiddenList.length ? 'Every course is hidden.' : 'No current courses.'),
      ]);
    }
    function hiddenTray(m) {
      if (!m.hiddenList.length) return null;
      return U.el('bcv-gpa__tray', [
        U.text('bcv-gpa__kicker2', 'Hidden · not counted in GPA', 'span'),
        ...m.hiddenList.map((c) => h('button', { type: 'button', class: 'bcv-gpa__traychip', title: `Show ${c.shortName || c.name} again`, onclick: () => showCourse(c) }, [
          h('span', { class: 'bcv-gpa__traydot', style: { background: c.color } }),
          h('span', { text: c.shortName || c.name }),
          h('span', { class: 'bcv-gpa__trayshow', text: 'Show' }),
        ])),
      ]);
    }
    async function hideCourse(c) {
      hidden.add(String(c.id));
      await saveHidden();
      await refresh();
      U.toast(`${c.shortName || c.name} is hidden and not counted in the GPA. Show it again from the tray below.`);
    }
    async function showCourse(c) {
      hidden.delete(String(c.id));
      await saveHidden();
      await refresh();
    }

    // ---- details sheet: the course's own grade page, without leaving the overview ----------
    function openDetail(c, from = null) {
      document.querySelector('.bcv-sheet-ov')?.remove();
      const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': `${c.shortName || c.name} grade details` });
      const close = () => ov.remove();
      ov.addEventListener('click', (e) => { if (e.target === ov) close(); });
      ov.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
      const sheet = U.el('bcv-sheet bcv-gpa-detail');
      ov.append(sheet);
      function bump(r, delta) {
        targets[c.id] = SCALE[clamp(r.idx + delta, 0, SCALE.length - 1)][0].replace(/−/g, '-'); // saved as the letter
        save();
        current = model();
        draw();
        paint();
        ov.focus(); // the stepper that had focus was just redrawn; keep Escape working
      }
      function paint() {
        const m = current || model();
        const r = m.rows.find((x) => x.c.id === c.id) || null; // null: no Canvas score yet, or a pass/fail course
        const pf = isPassFail(targets[c.id]);
        const pfPct = pf && c.score !== null && c.score !== undefined ? Number(c.score) : null;
        const gm = gmFor(c);
        const cats = gm.legend;
        const head = U.el('bcv-sheet__head bcv-gpa-detail__head', [
          h('div', { class: 'bcv-gpa-detail__ring' }, ringSvg(c, r ? r.pct : pfPct, cats, true)),
          U.el('bcv-sheet__titles', [
            U.text('bcv-gpa-detail__title bcv-ellip', c.shortName || c.name),
            U.text('bcv-gpa-detail__name', c.nickname ? c.originalName : (c.code || c.name)),
            U.el('bcv-gpa-detail__line', [
              U.text('bcv-gpa-detail__pct', r ? `${store.fmtPts(r.pct)}%` : pfPct !== null ? `${store.fmtPts(pfPct)}%` : 'N/A', 'span'),
              h('span', { class: 'bcv-gpa-detail__letter', style: r || pfPct !== null ? { background: c.palette.tint, color: c.palette.text } : null, text: r ? r.letter : pf ? 'Pass/Fail' : 'No grade yet' }),
              r ? U.el('bcv-gpa-detail__target', [
                U.text('bcv-gpa-detail__tlabel', 'Target', 'span'),
                h('button', { type: 'button', class: 'bcv-gpa-detail__step', text: '−', 'aria-label': 'Lower the target', disabled: r.idx >= SCALE.length - 1 || null, onclick: () => bump(r, 1) }),
                h('span', { class: 'bcv-gpa-detail__tval', style: { background: c.palette.tint, color: c.palette.text }, text: `${r.target[0]} · ${r.target[1]}%` }),
                h('button', { type: 'button', class: 'bcv-gpa-detail__step', text: '+', 'aria-label': 'Raise the target', disabled: r.idx <= 0 || null, onclick: () => bump(r, -1) }),
              ]) : null,
            ]),
          ]),
          h('button', { type: 'button', class: 'bcv-sheet__close', 'aria-label': 'Close', onclick: close }, U.svg(IC.close, { size: 13, stroke: 'var(--bcv-ink2)', width: 2.3 })),
        ]);
        const groupRows = cats.map((ct) => U.el('bcv-gpa-detail__grow', [
          h('span', { class: 'bcv-gpa__catdot bcv-gpa__catdot--9', style: { background: ct.color } }),
          U.text('bcv-gpa-detail__gname bcv-pretty', ct.label, 'span'),
          h('span', { class: 'bcv-gpa-detail__gbar' }, h('span', { style: { width: `${clamp(ct.pct ?? 0, 0, 100)}%`, background: ct.color } })),
          U.text('bcv-gpa-detail__gweight', ct.weightText, 'span'),
          U.text('bcv-gpa-detail__gpct', ct.value, 'span'),
        ]));
        const ungradedRows = gm.ungraded.map((u) => U.el('bcv-gpa-detail__grow bcv-gpa-detail__grow--ungraded', [
          h('span', { class: 'bcv-gpa__catdot bcv-gpa__catdot--9 bcv-gpa__catdot--empty' }),
          U.text('bcv-gpa-detail__gname bcv-pretty', u.name, 'span'),
          h('span', { class: 'bcv-gpa-detail__gbar' }),
          U.text('bcv-gpa-detail__gweight', u.weightText ? `${u.weightText} of grade · nothing graded` : 'nothing graded', 'span'),
          U.text('bcv-gpa-detail__gpct', '—', 'span'),
        ]));
        const byGroup = U.el('bcv-gpa-detail__sec', [
          U.text('bcv-gpa__kicker2', 'By group', 'span'),
          ...groupRows, ...ungradedRows,
          groupRows.length || ungradedRows.length ? null : U.text('bcv-gpa-detail__note', 'No assignment groups in this course.'),
          U.text(`bcv-gpa-detail__need bcv-pretty ${r ? needClass(r) : ''}`, r ? needText(r) : 'Nothing graded yet — no score to project from'),
        ]);
        const weights = gm.weighted
          ? U.el('bcv-gpa-detail__sec bcv-gpa-detail__sec--line', [
            U.el('bcv-gpa-detail__hrow', [U.text('bcv-gpa__kicker2', 'How the grade is weighted', 'span'), U.text('bcv-gpa-detail__hsub', `${gm.weightSum}% of final grade`, 'span')]),
            gm.weightBar.length ? U.el('bcv-wbar', gm.weightBar.map((w) => h('div', { class: `bcv-wbar__seg ${w.graded ? '' : 'bcv-wbar__seg--ungraded'}`, style: { flex: `${w.weight} 1 0`, background: w.color || '' }, title: `${w.name} · ${w.weight}%` }))) : U.text('bcv-gpa-detail__note', 'No assignment group carries weight yet.'),
            U.el('bcv-gpa-detail__legend', gm.weightBar.map((w) => U.el('bcv-gpa-detail__lg', [h('span', { class: `bcv-wbar__dot ${w.graded ? '' : 'bcv-wbar__dot--ungraded'}`, style: { background: w.color || '' } }), U.text('bcv-gpa-detail__lgtext', `${w.name} ${w.weight}% · ${w.graded ? 'graded' : 'nothing graded'}`, 'span')]))),
            gm.weightNote ? U.text('bcv-gpa-detail__note bcv-pretty', gm.weightNote) : null,
          ])
          : U.el('bcv-gpa-detail__sec bcv-gpa-detail__sec--line', [
            U.text('bcv-gpa__kicker2', 'How the grade is weighted', 'span'),
            U.text('bcv-gpa-detail__note bcv-pretty', 'This course does not weight its groups — the total is points earned over points possible.'),
          ]);
        const list = U.el('bcv-gpa-detail__sec bcv-gpa-detail__sec--line', [
          U.el('bcv-gpa-detail__hrow', [U.text('bcv-gpa__kicker2', 'Assignments', 'span'), U.text('bcv-gpa-detail__hsub', 'Blue dot means graded', 'span')]),
          // every row opens the assignment it is a line about, the same as on the course's own Grades
          // page: a grade is the start of a question, and the answer is on that page
          gm.rows.length ? U.el('bcv-gpa-detail__list', gm.rows.map((g) => h(g.url ? 'a' : 'div', {
            class: `bcv-gpa-detail__arow ${g.url ? 'is-link' : ''}`,
            href: g.url || null,
            style: g.url ? { color: 'inherit' } : null,
            onclick: g.url ? () => close() : null, // the sheet belongs to the page being left
          }, [
            h('span', { class: 'bcv-gpa__catdot', style: { background: g.earned !== null ? '#0a84ff' : 'transparent' } }),
            U.el('bcv-gpa-detail__abody', [U.text('bcv-gpa-detail__aname bcv-pretty', g.name), U.text('bcv-gpa-detail__agroup', `${g.group}${g.badge ? ` · ${g.badge}` : ''}`)]),
            U.text('bcv-gpa-detail__ascore', `${g.earned === null ? '—' : store.fmtPts(g.earned)} / ${store.fmtPts(g.possible)}`, 'span'),
            g.url ? U.chev() : null,
          ]))) : U.text('bcv-gpa-detail__note', 'No assignments in this course.'),
          h('a', { class: 'bcv-gpa-detail__link', href: `${c.url}/grades`, text: 'Open the course Grades page' }),
        ]);
        sheet.replaceChildren(head, U.el('bcv-sheet__list bcv-gpa-detail__body', [byGroup, weights, list]));
      }
      paint();
      document.body.append(ov);
      U.morphFrom(sheet, from); // the sheet grows out of the Details button
      ov.tabIndex = -1;
      ov.focus();
    }

    // ---- settings sheet ----------------------------------------------------------------------
    function openSettings(m, { wantTracking = false, from = null } = {}) {
      document.querySelector('.bcv-sheet-ov')?.remove();
      const ov = U.el('bcv-sheet-ov', null, { role: 'dialog', 'aria-label': 'GPA settings' });
      let pendingGoal = goal;
      let on = !!tracking || wantTracking;
      const priorGpa = h('input', { class: 'bcv-input bcv-gpa-set__input', id: 'bcv-gpa-prior', type: 'number', min: '0', max: '4', step: '0.01', placeholder: '3.42', value: hasPrior() ? String(tracking.priorGpa) : '' });
      const priorN = h('input', { class: 'bcv-input bcv-gpa-set__input', id: 'bcv-gpa-prior-n', type: 'number', min: '1', max: '200', step: '1', placeholder: '8', value: hasPrior() ? String(tracking.priorCourses) : '' });
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
          // the record before this term is optional: without it, cumulative fills in from snapshots
          const blank = !priorGpa.value.trim() && !priorN.value.trim();
          const g = Number(priorGpa.value), n = Math.round(Number(priorN.value));
          if (!blank && (!Number.isFinite(g) || g < 0 || g > 4 || !(n >= 1))) {
            U.toast('Enter your GPA before this term (0–4) and how many courses it covers, or leave both blank.', { error: true });
            priorGpa.focus();
            return;
          }
          tracking = blank ? { priorGpa: null, priorCourses: 0, since: tracking?.since || dayKey() } : { priorGpa: g, priorCourses: n, since: tracking?.since || dayKey() };
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
          h('span', { text: m.shownCount ? `${courseLabel(m)} · every course counts equally` : 'No current course to score' }),
          U.btn('Done', { kind: 'primary', cls: 'bcv-btn--fill36', onClick: done }),
        ]),
      ]));
      document.body.append(ov);
      U.morphFrom(ov.firstElementChild, from); // out of the gear, or the banner's button
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
    async function refresh() {
      current = model();
      await snapshot(current);
      if (ctx.alive()) draw();
    }
    function draw() {
      const m = current || model();
      sub.textContent = `${term ? `${term} · ` : ''}${m.shownCount ? courseLabel(m) : 'no current courses'}`;
      body.replaceChildren(...[
        tracking ? null : U.el('bcv-gpa__banner', [
          U.svg(BARS, { size: 19, stroke: 'var(--bcv-blue)', width: 1.9, style: { flex: 'none' } }),
          U.el('bcv-gpa__banner-body', [U.text('bcv-gpa__banner-title', 'Track GPA over time?'), U.text('bcv-gpa__banner-sub bcv-pretty', 'Canvas stores no GPA and no history. Add your GPA before this term and how many courses it covers, and this page keeps its own daily record.')]),
          U.btn('Turn on tracking', { kind: 'primary', cls: 'bcv-btn--fill36', onClick: (e) => openSettings(m, { wantTracking: true, from: e.currentTarget }) }),
        ]),
        U.el('bcv-gpa__top', [U.enter(hero(m), 1, 40, 400), U.enter(trend(), 1, 110, 400)]), // hero, then trend
        stats(m),
        courseGrid(m),
        hiddenTray(m),
        U.el('bcv-gpa__foot', [
          h('p', { class: 'bcv-pretty', text: tracking
            ? 'Course scores come straight from Canvas. GPA, targets and history are computed here from the standard 4.0 scale and the prior record you entered — your school’s official GPA may differ.'
            : 'Course scores and target maths come straight from Canvas. Term GPA is the plain 4.0-scale average of your courses; cumulative GPA and history stay empty until you turn on tracking.' }),
          tracking ? h('button', { type: 'button', class: 'bcv-gpa__linkbtn', text: 'Reset setup', onclick: resetTracking }) : null,
        ]),
      ].filter(Boolean));
      entered = true;
    }

    await refresh();
    return screen;
  }

  /** What the screen asks for first — the course list, then each followed course's assignment
   *  groups — so a press on Grades lands from the memo. The same list render() settles on. */
  async function prefetch() {
    const all = await store.courses().catch(() => null);
    if (!all) return;
    const currentCourses = all.filter((c) => c.state === 'current');
    const starredCourses = currentCourses.filter((c) => c.favorite);
    await Promise.all((starredCourses.length ? starredCourses : currentCourses).map((c) => store.assignmentGroups(c.id).catch(() => {})));
  }

  BCV.screens.gpa = { render, courseMath, SCALE, letterFor, pointsFor, targetIndex, prefetch };
})();
