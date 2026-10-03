/* The rubric as a ring, on the desktop: one slice per criterion, as long as that criterion's share
 * of the points, floating over the page with nothing round it — the page dims and blurs to bring it
 * into focus (a little blur right behind the ring, more around it). Marked and posted, the ring bends
 * out where the work did well and in where it lost points; a criterion not marked yet stays on the
 * circle. A slice, its label or Enter opens it: the ring turns that slice to its left side and
 * unrolls it into a bar while the rest of the ring sinks into the centre, and the rating levels come
 * out beside the bar at the height of their points. The dots switch criteria in place; the little
 * ring on top of the bar, Esc or a press beside it rolls the bar back up. A few tween values on one
 * frame loop drive every point of the geometry, drawn by hand each frame (no CSS transition on an SVG
 * path), so a press part-way turns it round from where it is. The band is filled pieces with a
 * gradient along each, its bend, thickness and colour carried smoothly from slice to slice. The SVG is only drawing: every slice is a real button (its label),
 * named for a screen reader. The phone keeps its bottom sheet (screens/course.js openRubric). */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h, htmlToText } = BCV.utils;
  const U = BCV.ui;
  const IC = BCV.IC;
  const store = BCV.store;
  const SVG = 'http://www.w3.org/2000/svg';

  // the stage, in its own units: scaled as a whole to the window
  const W = 760, H = 560, CX = 380, CY = 280, R = 176, BX = 66, BT = 158;
  const TAU = Math.PI * 2;
  const PALETTE = ['#5e5ce6', '#0a84ff', '#30b0c7', '#30d158', '#ff9f0a', '#ff375f'];
  const TRACK = [72, 72, 74]; // a bar's unearned stretch
  const BLACK = [0, 0, 0], WHITE = [255, 255, 255];

  const clamp01 = (v) => Math.max(0, Math.min(1, v));
  const sm = (v) => v * v * (3 - 2 * v);
  const span = (e, a, b) => sm(clamp01((e - a) / (b - a)));
  const f = (v) => v.toFixed(1);
  const num = (v) => (v === null || v === undefined || v === '' || !Number.isFinite(Number(v)) ? null : Number(v));
  const pts = (v) => store.fmtPts(v);
  const rgb = (hex) => [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16));
  const mix = (A, B, t) => [A[0] + (B[0] - A[0]) * t, A[1] + (B[1] - A[1]) * t, A[2] + (B[2] - A[2]) * t];
  const css = (A) => `rgb(${Math.round(A[0])},${Math.round(A[1])},${Math.round(A[2])})`;
  const hex = (A) => `#${A.map((v) => Math.round(v).toString(16).padStart(2, '0')).join('')}`;
  function hsl(hue, s, l) {
    const k = (q) => (q + hue / 30) % 12, a = s * Math.min(l, 1 - l);
    return [0, 8, 4].map((q) => 255 * (l - a * Math.max(-1, Math.min(k(q) - 3, Math.min(9 - k(q), 1)))));
  }
  const pt = (r, a) => [CX + r * Math.sin(a), CY - r * Math.cos(a)];
  const ease = (x) => (x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2);
  const plainLines = (s) => s.split(/\n+/).map((l) => l.replace(/^\s*[•\-*·]\s*/, '').trim()).filter(Boolean).join(' · ');

  /** A short name for the ring: the whole name when it is short, else its first words. */
  function shortOf(name) {
    if (name.length <= 16) return name;
    let out = '';
    for (const w of name.split(/\s+/)) {
      const next = out ? `${out} ${w}` : w;
      if (next.length > 14) break;
      out = next;
    }
    out = out.replace(/[\s/&,:;–—-]+$/, '');
    return out || `${name.slice(0, 13)}…`;
  }

  /** The rubric as the ring reads it, keyed by criterion id (instructors reorder criteria). Marked
   *  only once Canvas has posted the grade: an assessment on a held-back grade is not shown. */
  function model(a, sub) {
    const assess = sub?.rubric_assessment || {};
    const posted = !!sub && sub.posted_at !== null; // (absent where a Canvas posts with the grade)
    const list = (a.rubric || []).filter(Boolean);
    const crit = list.map((cr, k) => {
      const levels = (cr.ratings || []).filter((r) => r && (r.description || num(r.points) !== null)).map((r) => ({
        id: String(r.id ?? ''),
        pts: num(r.points),
        label: htmlToText(r.description || '', 140).replace(/\s+/g, ' ').trim() || 'Rating',
        text: htmlToText(r.long_description || '', 320).replace(/\s+/g, ' ').trim(),
      })).sort((x, y) => (y.pts ?? -Infinity) - (x.pts ?? -Infinity));
      const worth = num(cr.points) ?? levels[0]?.pts ?? 0;
      const got = posted ? assess[cr.id] || null : null;
      let score = num(got?.points);
      let mark = got && got.rating_id !== undefined && got.rating_id !== null ? levels.findIndex((l) => l.id === String(got.rating_id)) : -1;
      if (mark < 0 && score !== null) { // (no rating named: the level nearest the points given)
        let best = Infinity;
        levels.forEach((l, i) => { if (l.pts !== null && Math.abs(l.pts - score) < best) { best = Math.abs(l.pts - score); mark = i; } });
      }
      if (score === null && mark >= 0) score = levels[mark].pts;
      const name = htmlToText(cr.description || '', 200).replace(/\s+/g, ' ').trim() || `Criterion ${k + 1}`;
      return {
        id: String(cr.id ?? k), name, short: shortOf(name),
        desc: plainLines(htmlToText(cr.long_description || '', 900)),
        levels, worth, score, mark: score === null ? -1 : mark,
        comment: got?.comments ? String(got.comments).trim() : '',
      };
    });
    // how far up its bar a criterion's mark sits (the stretch above it was not earned)
    for (const c of crit) {
      const sc = scaleOf(c, 500);
      c.frac = c.score === null ? 1 : sc.rank ? (c.mark >= 0 && c.levels.length > 1 ? 1 - c.mark / (c.levels.length - 1) : 1) : clamp01((c.score - sc.lo) / Math.max(1e-9, sc.top - sc.lo));
    }
    const graded = crit.some((c) => c.score !== null);
    const max = crit.reduce((n, c) => n + Math.max(0, c.worth), 0);
    const earned = crit.reduce((n, c) => n + (c.score || 0), 0);
    const n = crit.length;
    const hues = crit.map((_, k) => (n <= PALETTE.length ? rgb(PALETTE[k]) : hsl((248 + (k * 360) / n) % 360, 0.78, 0.56)));
    // each slice's share of the circle: its points, with a sliver for a criterion worth nothing
    const weights = crit.map((c) => Math.max(0, c.worth));
    const total = weights.reduce((x, y) => x + y, 0);
    const shares = total > 0 ? weights.map((w) => Math.max(w, total * 0.03)) : weights.map(() => 1);
    const sum = shares.reduce((x, y) => x + y, 0);
    let acc = 0;
    const seg = shares.map((s) => { const a0 = (acc / sum) * TAU; acc += s; return [a0, (acc / sum) * TAU]; });
    // a criterion marked low bends in, one marked high bends out — damped as the count rises
    const bend = crit.map((c) => {
      if (!graded || c.score === null || c.worth <= 0) return 0;
      const fr = c.score / c.worth;
      const amp = Math.min(1, Math.max(0.35, (c.worth / Math.max(1e-9, max)) * n / 3));
      return Math.max(-34, Math.min(22, 110 * (fr - 0.85))) * Math.min(1, 6 / n + 0.25) * amp;
    });
    // a slice is thicker the more of the rubric it carries — growing inward, its outer edge on the circle
    const avg = sum / Math.max(1, n);
    const thick = shares.map((sh) => Math.max(5, Math.min(28, 10 * Math.pow(sh / Math.max(1e-9, avg), 1.5))));
    return { crit, graded, posted, max, earned, hues, seg, bend, thick, n, held: !posted && Object.keys(assess).length > 0 };
  }

  /** Where a criterion's levels sit on its bar: by points, the best at the top. */
  function scaleOf(c, BB) {
    const L = c.levels;
    if (!L.length) { // (marked freely: one bar, from nothing to what it is worth)
      const top = Math.max(c.worth, c.score || 0, 1e-9);
      return { lo: 0, top, y: (p) => BB - (BB - BT) * clamp01(p / top) };
    }
    const vals = L.map((l) => l.pts).filter((p) => p !== null);
    const top = vals.length ? Math.max(...vals) : 0, lo = vals.length ? Math.min(...vals) : 0;
    if (!vals.length || top - lo < 1e-9) { // (no points to place them by: evenly, in their order)
      const i0 = (p, i) => (L.length === 1 ? BT : BT + ((BB - BT) * i) / (L.length - 1));
      return { lo, top, rank: true, y: (p, i) => i0(p, i) };
    }
    return { lo, top, y: (p, i) => (p === null ? BT + ((BB - BT) * i) / Math.max(1, L.length - 1) : BB - ((BB - BT) * (p - lo)) / (top - lo)) };
  }

  let live = null; // the ring that is open

  function open(a, sub, { from } = {}) {
    live?.close(true);
    document.querySelector('.bcv-sheet-ov')?.remove();
    const m = model(a, sub);
    if (!m.n) return null;
    const { crit, hues, seg, n } = m;
    const reduce = U.reducedMotion();
    const hv = crit.map(() => 0); // how far each slice is swollen by the pointer (0 to 1)
    const st = { sel: 0, t: 0, g: reduce || !m.graded ? (m.graded ? 1 : 0) : 0, hov: -1, prev: -1, w: 1, BB: 500, BB0: 500 };

    // ---- the page around it: dimmed, blurred (more away from the ring), and the header over that
    const ov = h('div', { class: 'bcv-sheet-ov bcv-rr-ov', role: 'dialog', 'aria-modal': 'true', 'aria-label': `${a.name || 'Assignment'} rubric`, tabindex: '-1', 'data-count': n > 15 ? 'lots' : n > 10 ? 'many' : 'few' });
    const veils = ['soft', 'deep', 'dim'].map((k) => h('div', { class: `bcv-rr__veil bcv-rr__veil--${k}`, 'aria-hidden': 'true' }));
    const status = m.graded
      ? `Graded${sub?.graded_at ? ` ${U.fmtShort(sub.graded_at)}` : ''} · ${n === 1 ? '1 criterion' : `${n} criteria`}`
      : m.held ? `${n === 1 ? '1 criterion' : `${n} criteria`} · marks not posted yet` : `${n === 1 ? '1 criterion' : `${n} criteria`} · not graded yet`;
    const closeBtn = h('button', { type: 'button', class: 'bcv-rr__close', 'aria-label': 'Close the rubric', onclick: () => close() }, U.svg(IC.close, { size: 13, stroke: 'currentColor', width: 2.4 }));
    const top = U.el('bcv-rr__top', [
      U.text('bcv-rr__eyebrow', a.rubric_settings?.title && !/^rubric$/i.test(a.rubric_settings.title) ? a.rubric_settings.title : 'Rubric'),
      h('h2', { class: 'bcv-rr__title bcv-ellip', text: a.name || 'Rubric' }),
      U.text('bcv-rr__status', status),
    ]);
    const foot = U.text('bcv-rr__foot', '');

    // ---- the stage: the drawing, the labels and the centre over it, the bar's words beside it
    const svg = document.createElementNS(SVG, 'svg');
    svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
    svg.setAttribute('class', 'bcv-rr__svg');
    svg.setAttribute('aria-hidden', 'true');
    const mk = (tag, attrs, parent) => { const e = document.createElementNS(SVG, tag); for (const k in attrs) e.setAttribute(k, attrs[k]); parent?.append(e); return e; };
    /** An attribute, a style or a class written only when it changes (most of a frame's values do not). */
    const setA = (el, key, v) => { const c = el._a || (el._a = {}); if (c[key] !== v) { c[key] = v; el.setAttribute(key, v); } };
    const sty = (el, key, v) => { const c = el._s || (el._s = {}); if (c[key] !== v) { c[key] = v; el.style[key] = v; } };
    const cls = (el, key, on) => { const c = el._c || (el._c = {}); if (c[key] !== on) { c[key] = on; el.classList.toggle(key, on); } };
    // the band is drawn in filled pieces, each coloured by a gradient along it: the edges are one smooth
    // outline (no strokes meeting at an angle) and the colour runs on without steps
    const defs = mk('defs', {}, svg);
    const uid = `bcv-rr${Math.random().toString(36).slice(2, 8)}`;
    let gradN = 0;
    const STOPS = 5;
    const piece = (parent) => {
      const id = `${uid}-${gradN++}`;
      const gr = mk('linearGradient', { id, gradientUnits: 'userSpaceOnUse' }, defs);
      return { gr, stops: Array.from({ length: STOPS }, () => mk('stop', {}, gr)), p: mk('path', { class: 'bcv-rr__band', fill: `url(#${id})` }, parent) };
    };
    // the bar's thread up to the little ring over it (the way back), under the band so the bar's cap covers its foot
    const stemGrad = mk('linearGradient', { id: `${uid}-stem`, gradientUnits: 'userSpaceOnUse' }, defs);
    const stemStops = [0, 1].map((o) => mk('stop', { offset: String(o) }, stemGrad));
    const stem = mk('line', { class: 'bcv-rr__stem', stroke: `url(#${uid}-stem)` }, svg);
    const ring = mk('g', { class: 'bcv-rr__ring' }, svg);
    // a group per slice: one at rest is drawn once and then only turned, shrunk and faded as a whole.
    // A piece turns at most 20° (an even number of them, so one starts at the slice's middle), so a
    // straight gradient along it reads true to the curve
    const PIECE = Math.PI / 9;
    const restN = seg.map(([a0, a1]) => 2 * Math.max(1, Math.ceil((a1 - a0) / (2 * PIECE))));
    const sliceG = crit.map((_, k) => mk('g', { 'data-k': String(k) }, ring));
    const bands = crit.map((_, k) => Array.from({ length: restN[k] }, () => piece(sliceG[k])));
    const dots = crit.map((_, k) => mk('circle', { fill: css(hues[k]) }, sliceG[k]));
    // the slice on its way to the bar, drawn afresh each frame over the rest: its round ends, its pieces, its dot
    const moveG = mk('g', { class: 'bcv-rr__move', display: 'none' }, ring);
    const caps = [0, 1].map(() => mk('circle', {}, moveG));
    const MOVE_N = 10;
    const moves = Array.from({ length: MOVE_N }, () => piece(moveG));
    const moveDot = mk('circle', {}, moveG);
    const leaders = mk('g', { class: 'bcv-rr__leaders' }, svg);
    const ticksG = mk('g', { class: 'bcv-rr__ticks' }, svg);
    const hitG = mk('g', { class: 'bcv-rr__hits' }, svg);
    const wedge = (r0, r1, a0, a1) => {
      if (a1 - a0 >= TAU - 1e-6) return `${wedge(r0, r1, a0, a0 + Math.PI)} ${wedge(r0, r1, a0 + Math.PI, a1)}`; // (one criterion: the whole circle, in two halves)
      const A = pt(r1, a0), B = pt(r1, a1), C = pt(r0, a1), D = pt(r0, a0), L = a1 - a0 > Math.PI ? 1 : 0;
      return `M${f(A[0])} ${f(A[1])} A${r1} ${r1} 0 ${L} 1 ${f(B[0])} ${f(B[1])} L${f(C[0])} ${f(C[1])} A${r0} ${r0} 0 ${L} 0 ${f(D[0])} ${f(D[1])} Z`;
    };
    seg.forEach(([a0, a1], k) => {
      const p = mk('path', { d: wedge(R - 30, R + 44, a0, a1), fill: 'transparent', class: 'bcv-rr__hit' }, hitG);
      p.addEventListener('pointerenter', () => hover(k));
      p.addEventListener('pointerleave', () => hover(-1));
      p.addEventListener('click', () => select(k));
    });

    const labelOf = (c) => (m.graded ? (c.score === null ? `${pts(c.worth)} pts · not marked` : `${pts(c.score)} / ${pts(c.worth)}`) : `${pts(c.worth)} pts`);
    const crowd = n >= 15 ? 2 : 1;
    const labels = crit.map((c, k) => {
      const b = h('button', {
        type: 'button', class: 'bcv-rr__label', 'data-k': String(k), title: c.name,
        'aria-label': m.graded ? (c.score === null ? `${c.name}, not marked yet, worth ${pts(c.worth)}` : `${c.name}, ${pts(c.score)} of ${pts(c.worth)}`) : `${c.name}, worth ${pts(c.worth)} ${c.worth === 1 ? 'point' : 'points'}`,
        style: { '--rr-c': css(hues[k]), fontSize: n > 15 ? '10.5px' : n > 10 ? '11px' : '12px' },
        onclick: () => select(k),
        onpointerenter: () => hover(k), onpointerleave: () => hover(-1),
        onfocus: (e) => { st.sel = k; if (e.target.matches(':focus-visible')) hover(k); else draw(); },
        onblur: () => hover(-1),
      }, [U.text('bcv-rr__lname', c.short, 'span'), n > 15 ? null : U.text('bcv-rr__lpts', labelOf(c), 'span')]);
      return b;
    });
    const labelBox = U.el('bcv-rr__labels', labels);
    const pct = m.max > 0 ? Math.round((m.earned / m.max) * 100) : 0;
    const centre = U.el('bcv-rr__centre', [
      U.text('bcv-rr__ctop', m.graded ? 'Score' : 'Total', 'span'),
      U.text('bcv-rr__cbig', pts(m.graded ? m.earned : m.max), 'span'),
      U.text('bcv-rr__csub', m.graded ? `of ${pts(m.max)} · ${pct}%` : m.max === 1 ? 'point' : 'points', 'span'),
      U.text('bcv-rr__chint', 'Pick a colour to open it', 'span'),
    ]);

    // the bar's side: the way back (a little ring on top of the bar), its header (which criterion, its
    // description, the dots), its levels, the marker's note
    const chips = crit.map((c, k) => h('button', { type: 'button', class: 'bcv-rr__chip', title: c.name, 'aria-label': `${c.name}, criterion ${k + 1} of ${n}`, style: { '--rr-c': css(hues[k]) }, onclick: () => select(k) }));
    // The way back is the bar's own end, carried on up a thread into the ring in miniature, the open
    // criterion's slice missing from it at the thread's top: the bar is that slice, pulled out. On the
    // pointer the slice fills back into its place and the little ring swells — what a press does to the real one.
    const back = h('button', { type: 'button', class: 'bcv-rr__back', title: 'Back to the ring (Esc)', 'aria-label': 'Back to the ring', onclick: () => toRing() });
    const mini = mk('svg', { class: 'bcv-rr__mini', viewBox: '-22 -22 44 44', 'aria-hidden': 'true' }, back);
    const MR = 14.5, MGAP = n > 1 ? Math.min(0.09, TAU / n / 5) : 0;
    const mpt = (a) => `${f(MR * Math.sin(a))} ${f(-MR * Math.cos(a))}`;
    const arc = (a0, a1, cw = true) => `M${mpt(a0)} A${MR} ${MR} 0 ${Math.abs(a1 - a0) > Math.PI ? 1 : 0} ${cw ? 1 : 0} ${mpt(a1)}`;
    mk('circle', { class: 'bcv-rr__mtrack', r: String(MR) }, mini);
    const spin = mk('g', { class: 'bcv-rr__spin' }, mini);
    const marcs = seg.map(([a0, a1], k) => mk('path', { class: 'bcv-rr__marc', stroke: hex(hues[k]), d: n === 1 ? `${arc(0, Math.PI)} ${arc(Math.PI, TAU).replace(/^M[^A]+/, '')}` : arc(a0 + MGAP / 2, a1 - MGAP / 2) }, spin));
    const mfill = [0, 1].map(() => mk('path', { class: 'bcv-rr__mfill', pathLength: '1' }, spin));
    mk('path', { class: 'bcv-rr__mchev', d: 'M1.6 -5 L-3.4 0 L1.6 5' }, mini);
    let spun = 0; // (the little ring's turn so far, carried on so a switch turns it the short way)
    const swatch = h('span', { class: 'bcv-rr__swatch' });
    const count = U.text('bcv-rr__count', '', 'span');
    const name = h('h3', { class: 'bcv-rr__name', tabindex: '-1' });
    const desc = U.text('bcv-rr__desc', '');
    const more = h('button', { type: 'button', class: 'bcv-rr__more', hidden: true, onclick: () => { const on = desc.classList.toggle('is-open'); more.textContent = on ? 'Less' : 'More'; more.setAttribute('aria-expanded', String(on)); } }, 'More');
    const head = U.el('bcv-rr__head', [
      U.el('bcv-rr__headmain', [U.el('bcv-rr__eyeline', [swatch, count]), name, U.el('bcv-rr__descwrap', [desc, more])]),
      U.el('bcv-rr__tools', [U.el('bcv-rr__chips', chips, { style: { gap: n > 15 ? '3px' : n > 10 ? '4px' : '6px' } })]),
    ]);
    const rowsBox = U.el('bcv-rr__rows');
    const note = U.el('bcv-rr__note', null, { hidden: true });
    const barBox = U.el('bcv-rr__bar', [back, head, rowsBox, note]);

    const inner = U.el('bcv-rr__inner', [svg, centre, labelBox, barBox]);
    const stage = U.el('bcv-rr__stage', inner);
    const field = U.el('bcv-rr__field', stage);
    ov.append(...veils, top, field, foot, closeBtn);

    // ---- the bar's contents for the selected criterion
    let rows = [], ticks = [], place = [];
    function fillBar(k, swap) {
      const c = crit[k];
      ov.style.setProperty('--rr-sel', css(hues[k]));
      const [r, g, b] = hues[k].map((v) => { const c = v / 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); });
      ov.style.setProperty('--rr-on', 0.2126 * r + 0.7152 * g + 0.0722 * b > 0.3 ? '#1c1c1e' : '#fff'); // (dark words on a light colour: green and orange chips)
      count.textContent = `Criterion ${k + 1} of ${n} · ${labelOf(c)}`;
      name.textContent = c.name;
      desc.textContent = c.desc;
      desc.classList.remove('is-open');
      more.textContent = 'More';
      desc.hidden = !c.desc;
      chips.forEach((b, i) => { b.classList.toggle('is-on', i === k); b.setAttribute('aria-current', i === k ? 'true' : 'false'); });
      // the little ring turns this slice's gap down to the thread, the short way round
      const [a0, a1] = seg[k], mid = (a0 + a1) / 2;
      let to = 180 - (mid * 180) / Math.PI;
      to = spun + ((((to - spun) % 360) + 540) % 360) - 180;
      spun = to;
      spin.style.transform = `rotate(${f(to)}deg)`;
      marcs.forEach((p, i) => p.classList.toggle('is-gone', i === k));
      mfill[0].setAttribute('d', arc(mid, a1 - (n > 1 ? MGAP / 2 : 0) - (n === 1 ? 1e-3 : 0)));
      mfill[1].setAttribute('d', arc(mid, a0 + (n > 1 ? MGAP / 2 : 0) + (n === 1 ? 1e-3 : 0), false));
      note.hidden = !c.comment;
      note.replaceChildren(U.svg('M4 5h16v10H9l-5 4z', { size: 13, stroke: 'currentColor', width: 2, cls: 'bcv-rr__noteic' }), U.text('bcv-rr__notetext', c.comment, 'span'));
      // the levels: a free-form criterion has none, so its score (or what it is worth) stands alone
      const levels = c.levels.length ? c.levels
        : [m.graded && c.score !== null ? { pts: c.score, label: 'Your score', text: `Out of ${pts(c.worth)} — marked without set levels.`, free: true } : { pts: c.worth, label: 'Marked freely', text: 'No set levels: your instructor gives a score up to this.', free: true }];
      const old = rows;
      for (const r of old) { // (switching along the bar the old rows fade as the new ones come; from the ring there is nothing to see go)
        if (!swap || reduce) { r.remove(); continue; }
        r.classList.add('is-out');
        setTimeout(() => r.remove(), 220);
      }
      rows = levels.map((l, i) => {
        const picked = m.graded && (c.levels.length ? c.mark === i : c.score !== null);
        const dim = m.graded && c.score !== null && !picked;
        const row = U.el(`bcv-rr__row${picked ? ' is-picked' : ''}${dim ? ' is-dim' : ''}${swap ? ' is-swap' : ''}`, [
          U.text('bcv-rr__rpts', l.pts === null ? '–' : pts(l.pts), 'span'),
          U.el('bcv-rr__rbox', [
            U.el('bcv-rr__rline', [U.text('bcv-rr__rlab', l.label, 'span'), picked ? U.text('bcv-rr__mine', 'Your mark', 'span') : null]),
            l.text ? U.text('bcv-rr__rtext', l.text, 'span') : null,
          ]),
        ], { style: { '--rr-i': String(i) } });
        rowsBox.append(row);
        return row;
      });
      // the bar is shorter when a note sits under it
      const noteH = c.comment ? Math.min(92, note.offsetHeight || 18) : 0;
      st.BB0 = st.BB;
      st.BB = c.comment ? Math.min(466, 510 - noteH) : 500;
      layoutRows(c, levels);
      requestAnimationFrame(() => { more.hidden = !c.desc || desc.scrollHeight <= desc.clientHeight + 1; });
    }
    /** The rows at the height of their points; where two would overlap they are pushed apart (a
     *  leader joins each moved row to its tick), and where they cannot all fit their words shorten. */
    function layoutRows(c, levels) {
      const BB = st.BB, sc = scaleOf(c, BB);
      for (const cls of ['', 'is-tight', 'is-tighter']) {
        if (cls) rows.forEach((r) => r.classList.add(cls));
        const hs = rows.map((r) => r.offsetHeight || 44);
        const room = BB + 30 - (BT - 34), need = hs.reduce((x, y) => x + y, 0) + 6 * (hs.length - 1);
        if (need <= room || cls === 'is-tighter') {
          const want = levels.map((l, i) => sc.y(l.pts, i));
          const ys = want.slice();
          for (let i = 1; i < ys.length; i++) ys[i] = Math.max(ys[i], ys[i - 1] + hs[i - 1] / 2 + 6 + hs[i] / 2);
          const last = ys.length - 1;
          if (ys[last] + hs[last] / 2 > BB + 30) {
            ys[last] = BB + 30 - hs[last] / 2;
            for (let i = last - 1; i >= 0; i--) ys[i] = Math.min(ys[i], ys[i + 1] - hs[i + 1] / 2 - 6 - hs[i] / 2);
          }
          place = levels.map((l, i) => ({ tick: want[i], row: ys[i] }));
          rows.forEach((r, i) => r.style.setProperty('--rr-y', `${f(ys[i])}px`));
          break;
        }
      }
      // the ticks on the bar, and a leader to any row that had to move
      ticksG.replaceChildren();
      leaders.replaceChildren();
      ticks = levels.map((l, i) => {
        const picked = m.graded && (c.levels.length ? c.mark === i : c.score !== null);
        return mk('circle', { cx: BX, cy: f(place[i].tick), r: picked ? 8 : 5.5, class: picked ? 'is-picked' : '' }, ticksG);
      });
      place.forEach((p, i) => {
        if (Math.abs(p.row - p.tick) < 3) return;
        mk('path', { d: `M${BX + 12} ${f(p.tick)} C${BX + 28} ${f(p.tick)} ${86} ${f(p.row)} ${102} ${f(p.row)}`, 'data-i': String(i) }, leaders);
      });
    }

    // ---- drawing: every point of every slice from the tween values
    const mids = seg.map(([a0, a1]) => (a0 + a1) / 2);
    /** Where an angle falls between two slices' middles: [this one, the next, how far across on an
     *  S-curve]. A value given at each middle (the bend, the thickness, the colour) is carried round
     *  the circle through it, so the ring has no step or corner where two slices meet. */
    function between(th) {
      if (n === 1) return [0, 0, 0];
      let x = th - mids[0];
      x -= Math.floor(x / TAU) * TAU;
      let k = n - 1;
      while (k > 0 && mids[k] - mids[0] > x) k--;
      const lo = mids[k] - mids[0], hi = k + 1 < n ? mids[k + 1] - mids[0] : TAU;
      return [k, (k + 1) % n, (1 - Math.cos((Math.PI * (x - lo)) / (hi - lo))) / 2];
    }
    const at = (vals, b) => vals[b[0]] + (vals[b[1]] - vals[b[0]]) * b[2];
    function colourAt(k, v, ee, u) {
      const [a0, a1] = seg[k];
      const b = between(a0 + (a1 - a0) * v);
      let col = mix(hues[b[0]], hues[b[1]], b[2]); // (the ring's own run of colour…)
      // …becoming the bar's: its own colour, darker at the foot and lighter at the top
      if (ee > 0) col = mix(col, v < 0.5 ? mix(hues[k], BLACK, 0.36 * (0.5 - v)) : mix(hues[k], WHITE, 0.44 * (v - 0.5)), ee);
      if (ee > 0 && m.graded && v > crit[k].frac + 1e-6) col = mix(col, TRACK, 0.8 * u); // (the stretch above the mark was not earned)
      return col;
    }
    /** …and across a switch along the bar, the old criterion's colour crossing over to the new one's. */
    function colourOf(k, v, ee, u) {
      const col = colourAt(k, v, ee, u);
      return k === st.sel && st.w < 1 && st.prev >= 0 && st.prev !== k ? mix(colourAt(st.prev, v, 1, 1), col, st.w) : col;
    }
    const f2 = (v) => v.toFixed(2);
    /** One piece of band from v0 to v1 (and on a hair to v1e, under the next piece): its outline from
     *  `sample` (a centre, the way out, a width), its colour a gradient along it from `colour`. */
    function drawPiece(pc, sample, colour, v0, v1, v1e, len) {
      const steps = Math.max(3, Math.min(90, Math.ceil(len / 2.5)));
      let o = '', i = '';
      let c0 = null, c1 = null;
      for (let j = 0; j <= steps; j++) {
        const v = v0 + ((v1e - v0) * j) / steps;
        const s = sample(v);
        const hw = s[4] / 2;
        const ox = s[0] + s[2] * hw, oy = s[1] + s[3] * hw, ix = s[0] - s[2] * hw, iy = s[1] - s[3] * hw;
        o += `${j ? 'L' : 'M'}${f2(ox)} ${f2(oy)}`;
        i = `L${f2(ix)} ${f2(iy)}${i}`;
        if (!j) c0 = s;
      }
      c1 = sample(v1);
      setA(pc.p, 'd', `${o}${i}Z`);
      setA(pc.gr, 'x1', f(c0[0])); setA(pc.gr, 'y1', f(c0[1]));
      setA(pc.gr, 'x2', f(c1[0])); setA(pc.gr, 'y2', f(c1[1]));
      pc.stops.forEach((sp, q) => {
        const o2 = q / (STOPS - 1);
        setA(sp, 'offset', o2.toFixed(3));
        setA(sp, 'stop-color', css(colour(v0 + (v1 - v0) * o2)));
      });
      setA(pc.p, 'display', 'inline');
    }
    /** A slice at rest: unturned and full size (its group does the rest), every piece and its dot. */
    function restSlice(k, go) {
      const [a0, a1] = seg[k], bump = hv[k], TH = m.thick, NB = restN[k];
      const sample = (v) => {
        const th = a0 + (a1 - a0) * v, b = between(th);
        const s1 = Math.sin(Math.PI * Math.min(1, v)), sw = bump * s1 * s1; // (the slice under the pointer swells out, smoothly, and back)
        const T = at(TH, b);
        const rad = R + at(go, b) - (T - 10) / 2 + 9 * sw; // (thicker inward: the outer edge stays on the circle)
        const sn = Math.sin(th), cs = Math.cos(th);
        return [CX + rad * sn, CY - rad * cs, sn, -cs, T + 2.5 * sw];
      };
      const colour = (v) => colourAt(k, v, 0, 0);
      const len = R * (a1 - a0);
      const eps = n > 1 ? 0.5 / len : 0; // (each piece runs on a hair under the next, so no seam shows between them)
      bands[k].forEach((pc, i) => drawPiece(pc, sample, colour, i / NB, (i + 1) / NB, (i + 1) / NB + eps, len / NB));
      const D = pt(R + go[k] + 22 + 9 * bump, mids[k]);
      setA(dots[k], 'cx', f(D[0])); setA(dots[k], 'cy', f(D[1]));
    }
    // the bar's top, where the thread to the little ring starts, and the little ring's foot
    const MINI = [BX, 56 + MR];
    function draw() {
      const e = st.t, sel = st.sel;
      const p1 = span(e, 0, 0.5); // the ring turns the slice to its left side…
      const pf = span(e, 0, 0.2); // (its bend evened out first, so the straightening starts from a true arc)
      const u = span(e, 0.22, 1); // …while it straightens into the bar…
      const q = span(e, 0.02, 0.62); // …and the rest of the ring sinks into the centre
      const go = m.bend.map((b) => b * st.g);
      let dRot = 1.5 * Math.PI - mids[sel];
      while (dRot > Math.PI) dRot -= TAU;
      while (dRot < -Math.PI) dRot += TAU;
      const rot = dRot * p1, rest = dRot * (p1 - 1);
      const BB = st.BB0 + (st.BB - st.BB0) * st.w;
      const shrinkRest = 1 - 0.34 * q, opRest = clamp01(1 - q * 1.25);
      const turn = `translate(${CX} ${CY}) rotate(${f((rot * 180) / Math.PI)}) scale(${shrinkRest.toFixed(4)}) translate(${-CX} ${-CY})`;
      const moving = e > 0;
      const dotA = f(clamp01(1 - q * 1.8));
      crit.forEach((c, k) => {
        const g = sliceG[k];
        if (moving && k === sel) { setA(g, 'display', 'none'); return; } // (drawn on its way, below)
        const key = `${st.g.toFixed(4)}|${hv[k].toFixed(4)}`;
        if (g._key !== key) { restSlice(k, go); g._key = key; } // (only when its bend or its swell has moved)
        setA(g, 'display', opRest < 0.005 && moving ? 'none' : 'inline');
        setA(g, 'transform', moving ? turn : '');
        setA(g, 'opacity', moving ? f(opRest) : '1');
        setA(dots[k], 'r', k === sel || st.hov === k ? '4.5' : n > 15 ? '2.5' : '3.5');
        setA(dots[k], 'opacity', dotA);
      });
      // the one on its way to the bar: every point, every frame, over the rest
      let top = null;
      if (moving) {
        const [a0, a1] = seg[sel], k = sel, TH = m.thick, bump = hv[k];
        const Rs = R + go[k];
        const L = Rs * (a1 - a0) + (BB - BT - Rs * (a1 - a0)) * u;
        const kap = (1 - u) / Rs;
        const Mx = CX - Rs + (BX - (CX - Rs)) * u, My = CY + ((BB + BT) / 2 - CY) * u;
        const cr = Math.cos(rest), sr = Math.sin(rest);
        const sample = (v) => {
          const th = a0 + (a1 - a0) * v, b = between(th);
          const s1 = Math.sin(Math.PI * Math.max(0, Math.min(1, v))), sw = bump * s1 * s1;
          const T = at(TH, b);
          const wd = T + 2.5 * sw + (10 - T - 2.5 * sw) * pf + 6 * u;
          if (u <= 0) { // still round the centre: turning, its bend evening out to a true arc
            let rad = R + at(go, b) - (T - 10) / 2 + 9 * sw;
            rad += (Rs - rad) * pf;
            const a = th + rot, sn = Math.sin(a), cs = Math.cos(a);
            return [CX + rad * sn, CY - rad * cs, sn, -cs, wd];
          }
          // the slice as it would be turned all the way, unrolling about its middle…
          const s = (v - 0.5) * L;
          let x, y, nx, ny;
          if (kap < 1e-5) { x = Mx; y = My - s; nx = -1; ny = 0; } else {
            const ks = kap * s;
            x = Mx + (1 - Math.cos(ks)) / kap; y = My - Math.sin(ks) / kap;
            nx = -Math.cos(ks); ny = -Math.sin(ks);
          }
          if (Math.abs(rest) < 1e-4) return [x, y, nx, ny, wd];
          // …then turned back by what the ring has still to turn, so the two motions overlap without a jump
          const dx = x - CX, dy = y - CY;
          return [CX + dx * cr - dy * sr, CY + dx * sr + dy * cr, nx * cr - ny * sr, nx * sr + ny * cr, wd];
        };
        const ee = u;
        const colour = (v) => colourOf(k, v, ee, u);
        // the pieces: eighths, split where the colour steps (the mark on the bar, the old one's mid-switch)
        const cuts = [0, 0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875, 1];
        const step = (fr) => { if (fr > 1e-3 && fr < 1 - 1e-3 && cuts.every((x) => Math.abs(x - fr) > 1e-3)) cuts.push(fr); };
        if (m.graded) { step(crit[k].frac); if (st.w < 1 && st.prev >= 0) step(crit[st.prev].frac); }
        cuts.sort((x, y) => x - y);
        const capT = span(e, 0.02, 0.3); // (its ends round off as it comes away from its neighbours)
        const lenV = Math.max(L, 1);
        moves.forEach((pc, i) => {
          if (i >= cuts.length - 1) { setA(pc.p, 'display', 'none'); return; }
          const v0 = cuts[i], v1 = cuts[i + 1];
          const v1e = i === cuts.length - 2 ? 1 + (0.5 / lenV) * (1 - capT) : v1 + 0.4 / lenV;
          drawPiece(pc, sample, colour, v0, v1, v1e, lenV * (v1 - v0));
        });
        [0, 1].forEach((v, i) => {
          const s = sample(v);
          setA(caps[i], 'cx', f2(s[0])); setA(caps[i], 'cy', f2(s[1]));
          setA(caps[i], 'r', f2((s[4] / 2) * capT));
          setA(caps[i], 'fill', css(colour(v)));
        });
        top = sample(1);
        const D = pt(R + go[k] + 22 + 9 * bump, mids[k] + rot);
        setA(moveDot, 'cx', f(D[0])); setA(moveDot, 'cy', f(D[1]));
        setA(moveDot, 'r', '4.5'); setA(moveDot, 'fill', css(hues[k])); setA(moveDot, 'opacity', dotA);
      }
      setA(moveG, 'display', moving ? 'inline' : 'none');
      // the labels ride round with their slices and fade as the ring goes
      const ringA = clamp01(1 - q * 1.8);
      labels.forEach((b, k) => {
        const mid = mids[k] + rot;
        const P = pt((R + go[k] + 36 + 9 * hv[k] * (1 - q)) * (1 - 0.2 * q), mid);
        const s = Math.sin(mid), co = Math.cos(mid);
        const tx = -50 + 50 * Math.max(-1, Math.min(1, s / 0.3)), ty = -50 - 50 * Math.max(-1, Math.min(1, co / 0.3));
        sty(b, 'transform', `translate(${f(P[0])}px, ${f(P[1])}px) translate(${f(tx)}%, ${f(ty)}%)`);
        sty(b, 'alignItems', s > 0.3 ? 'flex-start' : s < -0.3 ? 'flex-end' : 'center');
        sty(b, 'textAlign', s > 0.3 ? 'left' : s < -0.3 ? 'right' : 'center');
        const shown = n <= 10 || k === st.sel || (k !== (st.sel + 1) % n && k !== (st.sel + n - 1) % n && k % crowd === 0 && !(k === n - 1 && n % crowd !== 0));
        cls(b, 'is-quiet', !shown);
        cls(b, 'is-hot', st.hov === k);
      });
      sty(labelBox, 'opacity', f(ringA));
      sty(centre, 'opacity', f(ringA));
      sty(centre, 'transform', `translate(-50%, -50%) scale(${f(1 - 0.12 * q)})`);
      const ringOn = e < 0.3;
      if (labelBox.inert === ringOn) labelBox.inert = !ringOn;
      sty(hitG, 'pointerEvents', e < 0.02 ? 'auto' : 'none');
      // the bar's words come out from the bar once it is nearly straight, row by row
      const barA = span(u, 0.7, 1);
      sty(head, 'opacity', f(barA));
      sty(head, 'transform', `translateY(${f((1 - barA) * 8)}px)`);
      sty(note, 'opacity', f(barA));
      rows.forEach((r, i) => {
        if (r.classList.contains('is-out')) return;
        const ra = span(u, 0.62 + 0.06 * i, 0.92 + 0.06 * i) * span(st.w, 0.25 + 0.08 * i, 0.75 + 0.08 * i);
        sty(r, 'opacity', f(ra));
        sty(r, 'transform', `translate(${f((1 - ra) * -18)}px, -50%)`);
      });
      ticks.forEach((t, i) => {
        const ta = span(u, 0.8 + 0.04 * i, 0.96 + 0.04 * i) * span(st.w, 0.3 + 0.06 * i, 0.8 + 0.06 * i);
        sty(t, 'opacity', f(ta));
        sty(t, 'transform', `scale(${f(0.2 + 0.8 * ta)})`);
      });
      sty(leaders, 'opacity', f(barA * span(st.w, 0.4, 1)));
      // the thread from the bar's top up into the little ring, drawn out of the bar as it straightens
      const grow = span(u, 0.76, 1);
      if (top && grow > 0) {
        const x2 = top[0] + (MINI[0] - top[0]) * grow, y2 = top[1] + (MINI[1] - top[1]) * grow;
        setA(stem, 'x1', f(top[0])); setA(stem, 'y1', f(top[1]));
        setA(stem, 'x2', f(x2)); setA(stem, 'y2', f(y2));
        setA(stemGrad, 'x1', f(top[0])); setA(stemGrad, 'y1', f(top[1]));
        setA(stemGrad, 'x2', f(MINI[0])); setA(stemGrad, 'y2', f(MINI[1]));
        const selC = st.w < 1 && st.prev >= 0 ? mix(hues[st.prev], hues[sel], st.w) : hues[sel];
        setA(stemStops[0], 'stop-color', css(colourOf(sel, 1, u, u)));
        setA(stemStops[1], 'stop-color', css(selC));
        setA(stem, 'opacity', f(grow));
        setA(stem, 'display', 'inline');
      } else setA(stem, 'display', 'none');
      const backA = span(u, 0.86, 1);
      sty(back, 'opacity', f(backA));
      sty(back, 'transform', `scale(${f(0.6 + 0.4 * backA)})`);
      const live = u >= 0.8;
      if (barBox.inert === live) barBox.inert = !live;
      cls(barBox, 'is-live', live);
      cls(ov, 'is-bar', e > 0.5);
      const state = e >= 0.999 ? 'bar' : e <= 0.001 ? 'ring' : 'moving';
      if (ov.dataset.state !== state) ov.dataset.state = state;
      const words = e > 0.5
        ? 'Height on the bar is points. Switch with the dots; the little ring at the top or Esc goes back.'
        : m.graded ? 'The ring pushes out where you scored well and pulls in where you lost points.'
          : 'Each colour’s stretch is its share of the points. Pick one to open it.';
      if (foot._t !== words) { foot._t = words; foot.textContent = words; }
    }

    // ---- motion: every value on one frame loop, drawn once a frame. A tween cut short by another
    // starts from where it is, so a quick press never leaves it half-way or makes it jump
    const tw = {};
    let raf = 0, lastNow = 0;
    function frame(now) {
      raf = 0;
      const dt = lastNow ? Math.min(64, now - lastNow) : 16;
      lastNow = now;
      let busy = false;
      const done = [];
      for (const key of Object.keys(tw)) {
        const a = tw[key];
        if (now < a.t0) { busy = true; continue; }
        const k = clamp01((now - a.t0) / a.dur);
        st[key] = a.from + (a.to - a.from) * ease(k);
        if (k >= 1) { delete tw[key]; if (a.done) done.push(a.done); } else busy = true;
      }
      // the hover swell eases in and out (each slice its own amount, so one leaving and the next arriving overlap)
      const ah = 1 - Math.exp(-dt / 115);
      hv.forEach((v, k) => {
        const to = st.hov === k && st.t < 0.02 ? 1 : 0;
        if (v === to) return;
        const nv = v + (to - v) * ah;
        hv[k] = Math.abs(to - nv) < 0.003 ? to : nv;
        if (hv[k] !== to) busy = true;
      });
      draw();
      if (busy) kick(); else lastNow = 0;
      done.forEach((fn) => fn());
    }
    function kick() { if (!raf && !gone) raf = requestAnimationFrame(frame); }
    function tween(key, to, dur, done, delay = 0) {
      delete tw[key];
      if (reduce || dur <= 0) { st[key] = to; draw(); done?.(); return; }
      tw[key] = { from: st[key], to, t0: performance.now() + delay, dur, done };
      kick();
    }
    function hover(k) {
      if (st.hov === k) return;
      st.hov = k;
      if (reduce) { hv.forEach((_, i) => { hv[i] = i === k && st.t < 0.02 ? 1 : 0; }); draw(); return; }
      kick();
    }
    function select(k) {
      k = ((k % n) + n) % n;
      if (k === st.sel && st.t > 0.98) return;
      if (st.t > 0.98 && k !== st.sel) { // the bar is out: switch in place, the colour crossing over, the rows changing
        st.prev = st.sel;
        st.sel = k;
        st.w = 0;
        fillBar(k, true);
        tween('w', 1, 520);
        name.focus({ preventScroll: true });
        return;
      }
      if (st.t > 0.02 && k !== st.sel) { tween('t', 0, 480, () => select(k)); return; } // (part-way: back round first)
      st.sel = k;
      st.prev = -1;
      st.w = 1;
      fillBar(k, false);
      st.BB0 = st.BB;
      ov.focus({ preventScroll: true }); // (held by the ring itself while the label it was on goes out of reach)
      tween('t', 1, 1150, () => name.focus({ preventScroll: true }));
    }
    function toRing() {
      if (st.t <= 0.001) return;
      const k = st.sel;
      ov.focus({ preventScroll: true });
      tween('t', 0, 950, () => labels[k].focus({ preventScroll: true }));
    }

    // ---- fitting the stage to the window
    let scale = 1;
    function fit() {
      const r = field.getBoundingClientRect();
      scale = Math.max(0.4, Math.min(1.12, r.width / W, r.height / H));
      ov.style.setProperty('--rr-s', String(scale));
      ov.style.setProperty('--rr-x', `${f(r.left + r.width / 2)}px`);
      ov.style.setProperty('--rr-y', `${f(r.top + r.height / 2)}px`);
      ov.style.setProperty('--rr-w', `${f(W * scale)}px`);
      ov.style.setProperty('--rr-h', `${f(H * scale)}px`);
    }
    const onResize = () => fit();

    // ---- keys: the arrows walk the criteria, Enter opens one (its label is a button), Escape goes back, then out
    ov.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); if (st.t > 0.02) toRing(); else close(); return; }
      if (['ArrowRight', 'ArrowDown', 'ArrowLeft', 'ArrowUp'].includes(e.key) && !(e.target instanceof HTMLInputElement)) {
        e.preventDefault();
        const d = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : -1;
        const k = (st.sel + d + n) % n;
        if (st.t > 0.5) select(k);
        else { st.sel = k; labels[k].focus({ preventScroll: true }); draw(); }
        return;
      }
      if (e.key === 'Tab') { // (focus stays inside: the page under it is out of reach)
        const all = [...ov.querySelectorAll('button')].filter((b) => !b.closest('[inert]') && !b.hidden && b.offsetParent !== null);
        if (!all.length) return;
        const i = all.indexOf(document.activeElement);
        if (e.shiftKey && (i <= 0)) { e.preventDefault(); all[all.length - 1].focus(); } else if (!e.shiftKey && i === all.length - 1) { e.preventDefault(); all[0].focus(); }
      }
    });
    // a press on the page around it: the bar rolls back up, the ring leaves (a press inside the ring is not beside it)
    ov.addEventListener('click', (e) => {
      if (e.target.closest('button, .bcv-rr__row, .bcv-rr__head, .bcv-rr__note, .bcv-rr__hit')) return;
      if (st.t > 0.02) { toRing(); return; }
      const r = svg.getBoundingClientRect(), s = r.width / W;
      if (Math.hypot((e.clientX - r.left) / s - CX, (e.clientY - r.top) / s - CY) < R + 50) return;
      close();
    });

    let gone = false;
    function close(now) {
      if (gone) return;
      gone = true;
      if (raf) cancelAnimationFrame(raf);
      raf = 0;
      removeEventListener('resize', onResize);
      if (live === api) live = null;
      const refocus = () => { try { if (from && from.isConnected) from.focus({ preventScroll: true }); } catch { /* gone */ } };
      if (now || reduce) { ov.remove(); refocus(); return; }
      ov.classList.add('is-closing', 'is-leaving');
      setTimeout(() => ov.remove(), 430);
      refocus();
    }
    /** Holds one tween value where it is put (the developer tools and the tests look at a frame part-way). */
    const seek = (key, v) => { delete tw[key]; st[key] = v; draw(); };
    const api = { close, el: ov, select, toRing, seek, state: st };
    live = api;

    document.body.append(ov);
    addEventListener('resize', onResize);
    fit();
    fillBar(0, false);
    draw();
    // the ring blooms in; marked, it then bends to its marks
    if (m.graded && !reduce) tween('g', 1, 1150, null, 450);
    ov.focus({ preventScroll: true });
    // a navigation away takes it too (app.js removes every sheet overlay): stop its frames
    const watch = new MutationObserver(() => { if (!ov.isConnected) { watch.disconnect(); if (!gone) close(true); } });
    watch.observe(document.body, { childList: true });
    return api;
  }

  BCV.rubricRing = { open, model, shortOf, get live() { return live; } };
})();
