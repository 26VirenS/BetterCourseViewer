/* The rubric as a ring, on the desktop: one slice per criterion, as long as that criterion's share
 * of the points, floating over the page with nothing round it — the page dims and blurs to bring it
 * into focus (a little blur right behind the ring, more around it). Marked and posted, the ring bends
 * out where the work did well and in where it lost points; a criterion not marked yet stays on the
 * circle. A slice, its label or Enter opens it: the ring turns that slice to its left side and
 * unrolls it into a bar while the rest of the ring sinks into the centre, and the rating levels come
 * out beside the bar at the height of their points. The dots switch criteria in place; the ring
 * button, Esc or a press beside it rolls the bar back up. One tween value drives every point of the
 * geometry, drawn by hand each frame (no CSS transition on an SVG path), so a press part-way turns
 * it round from where it is. The SVG is only drawing: every slice is a real button (its label),
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
    return { crit, graded, posted, max, earned, hues, seg, bend, n, held: !posted && Object.keys(assess).length > 0 };
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
    const st = { sel: 0, t: 0, g: reduce || !m.graded ? (m.graded ? 1 : 0) : 0, hov: -1, prev: -1, w: 1, BB: 500, BB0: 500 };
    const rafs = {};
    const M = Math.max(10, Math.round(192 / n)); // pieces per slice: enough for a smooth curve and a colour blend

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
    // the straight bar's colour as one gradient (its pieces would show as bands once it is still)
    const gid = `bcv-rr-g${Math.random().toString(36).slice(2, 8)}`;
    const grad = mk('linearGradient', { id: gid, gradientUnits: 'userSpaceOnUse' }, mk('defs', {}, svg));
    const STOPS = 12;
    const stops = Array.from({ length: STOPS + 2 }, () => mk('stop', {}, grad));
    const ring = mk('g', { class: 'bcv-rr__ring' }, svg);
    const unders = crit.map(() => mk('path', { fill: 'none', 'stroke-linejoin': 'round', 'stroke-linecap': 'butt' }, ring));
    const lines = crit.map(() => Array.from({ length: M }, () => mk('line', {}, ring)));
    const dots = crit.map((_, k) => mk('circle', { fill: css(hues[k]) }, ring));
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

    // the bar's side: its header (which criterion, its description, the dots, the ring button), its levels, the marker's note
    const chips = crit.map((c, k) => h('button', { type: 'button', class: 'bcv-rr__chip', title: c.name, 'aria-label': `${c.name}, criterion ${k + 1} of ${n}`, style: { '--rr-c': css(hues[k]) }, onclick: () => select(k) }));
    const back = h('button', { type: 'button', class: 'bcv-rr__back', title: 'Back to the ring', 'aria-label': 'Back to the ring', onclick: () => toRing() },
      U.svg('M12 5a7 7 0 1 0 0 14a7 7 0 1 0 0-14', { size: 15, stroke: 'currentColor', width: 2.2 }));
    const swatch = h('span', { class: 'bcv-rr__swatch' });
    const count = U.text('bcv-rr__count', '', 'span');
    const name = h('h3', { class: 'bcv-rr__name', tabindex: '-1' });
    const desc = U.text('bcv-rr__desc', '');
    const more = h('button', { type: 'button', class: 'bcv-rr__more', hidden: true, onclick: () => { const on = desc.classList.toggle('is-open'); more.textContent = on ? 'Less' : 'More'; more.setAttribute('aria-expanded', String(on)); } }, 'More');
    const head = U.el('bcv-rr__head', [
      U.el('bcv-rr__headmain', [U.el('bcv-rr__eyeline', [swatch, count]), name, U.el('bcv-rr__descwrap', [desc, more])]),
      U.el('bcv-rr__tools', [U.el('bcv-rr__chips', chips, { style: { gap: n > 15 ? '3px' : n > 10 ? '4px' : '6px' } }), back]),
    ]);
    const rowsBox = U.el('bcv-rr__rows');
    const note = U.el('bcv-rr__note', null, { hidden: true });
    const barBox = U.el('bcv-rr__bar', [head, rowsBox, note]);

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
    function colourAt(k, vm, ee, u) {
      const prevH = hues[(k + n - 1) % n], nextH = hues[(k + 1) % n];
      const blendW = 0.9 * (1 - ee);
      let col = vm < 0.5 ? mix(hues[k], prevH, (0.5 - vm) * blendW) : mix(hues[k], nextH, (vm - 0.5) * blendW);
      if (ee > 0) col = mix(col, vm < 0.5 ? mix(hues[k], BLACK, 0.18) : mix(hues[k], WHITE, 0.22), ee * Math.abs(vm - 0.5) * 2);
      if (ee > 0 && m.graded && vm > crit[k].frac + 1e-6) col = mix(col, TRACK, 0.8 * u); // (the stretch above the mark was not earned)
      return col;
    }
    /** …and across a switch along the bar, the old criterion's colour crossing over to the new one's. */
    function colourOf(k, vm, ee, u) {
      const col = colourAt(k, vm, ee, u);
      return k === st.sel && st.w < 1 && st.prev >= 0 && st.prev !== k ? mix(colourAt(st.prev, vm, 1, 1), col, st.w) : col;
    }
    function draw() {
      const e = st.t, sel = st.sel;
      const p1 = span(e, 0, 0.5); // the ring turns the slice to its left side…
      const pf = span(e, 0, 0.2); // (its bend evened out first, so the straightening starts from a true arc)
      const u = span(e, 0.22, 1); // …while it straightens into the bar…
      const q = span(e, 0.02, 0.62); // …and the rest of the ring sinks into the centre
      const go = m.bend.map((b) => b * st.g);
      const selMid = (seg[sel][0] + seg[sel][1]) / 2;
      let dRot = 1.5 * Math.PI - selMid;
      while (dRot > Math.PI) dRot -= TAU;
      while (dRot < -Math.PI) dRot += TAU;
      const rot = dRot * p1, rest = dRot * (p1 - 1);
      const BB = st.BB0 + (st.BB - st.BB0) * st.w;
      crit.forEach((c, k) => {
        const [a0, a1] = seg[k];
        const isSel = k === sel;
        const ee = isSel ? u : 0;
        const po = go[(k + n - 1) % n], no = go[(k + 1) % n], ko = go[k];
        const shrink = isSel ? 1 : 1 - 0.34 * q;
        const op = isSel ? 1 : clamp01(1 - q * 1.25);
        const offAt = (v) => {
          const bl = v < 0.5 ? 1 - Math.cos(Math.PI * (0.5 - v)) : 1 - Math.cos(Math.PI * (v - 0.5));
          return v < 0.5 ? ko + ((po - ko) * bl) / 2 : ko + ((no - ko) * bl) / 2;
        };
        const Rs = R + ko;
        const P = (v) => {
          const ang = a0 + (a1 - a0) * v + rot;
          let rad = R + offAt(v);
          if (isSel) rad += (Rs - rad) * pf;
          if (!isSel || u <= 0) return pt(rad * shrink, ang);
          // the slice as it would be turned all the way, unrolling about its middle…
          const L = Rs * (a1 - a0) + (BB - BT - Rs * (a1 - a0)) * u;
          const kap = (1 - u) / Rs;
          const Mx = CX - Rs + (BX - (CX - Rs)) * u, My = CY + ((BB + BT) / 2 - CY) * u;
          const s = (v - 0.5) * L;
          const x = kap < 1e-5 ? Mx : Mx + (1 - Math.cos(kap * s)) / kap;
          const y = kap < 1e-5 ? My - s : My - Math.sin(kap * s) / kap;
          if (Math.abs(rest) < 1e-4) return [x, y];
          // …then turned back by what the ring has still to turn, so the two motions overlap without a jump
          const cs = Math.cos(rest), sn = Math.sin(rest), dx = x - CX, dy = y - CY;
          return [CX + dx * cs - dy * sn, CY + dx * sn + dy * cs];
        };
        const width = (isSel ? 10 + 6 * u : 10 * (1 - 0.7 * q)) + (st.hov === k && e < 0.02 ? 3 : 0);
        let d = '';
        for (let j = 0; j <= M; j++) { const Q = P(j / M); d += `${j ? ' L' : 'M'}${f(Q[0])} ${f(Q[1])}`; }
        const ul = unders[k];
        ul.setAttribute('d', d);
        ul.setAttribute('stroke-width', f(width));
        ul.setAttribute('stroke-opacity', f(op));
        ul.setAttribute('stroke-linecap', isSel && u > 0.02 ? 'round' : 'butt');
        // nearly straight, the bar hands over from its pieces to one gradient along it
        const smooth = isSel ? span(u, 0.85, 1) : 0;
        if (smooth > 0) {
          const A = P(0), B = P(1), frac = m.graded ? crit[k].frac : 1;
          grad.setAttribute('x1', f(A[0])); grad.setAttribute('y1', f(A[1]));
          grad.setAttribute('x2', f(B[0])); grad.setAttribute('y2', f(B[1]));
          const at = Array.from({ length: STOPS }, (_, i) => i / (STOPS - 1));
          if (frac < 1) at.push(Math.max(0, frac - 1e-4), Math.min(1, frac + 2e-4)); else at.push(1, 1);
          at.sort((x, y) => x - y);
          stops.forEach((sp, i) => {
            sp.setAttribute('offset', at[i].toFixed(4));
            sp.setAttribute('stop-color', css(colourOf(k, at[i], ee, u)));
          });
          ul.setAttribute('stroke', `url(#${gid})`);
        } else ul.setAttribute('stroke', css(hues[k]));
        const segs = lines[k];
        for (let j = 0; j < M; j++) {
          const v0 = j / M, v1 = (j + 1) / M, vm = (v0 + v1) / 2;
          const col = colourOf(k, vm, ee, u);
          const A = P(Math.max(0, v0 - 0.006)), B = P(Math.min(1, v1 + 0.006));
          const l = segs[j];
          l.setAttribute('x1', f(A[0])); l.setAttribute('y1', f(A[1]));
          l.setAttribute('x2', f(B[0])); l.setAttribute('y2', f(B[1]));
          l.setAttribute('stroke', css(col));
          l.setAttribute('stroke-width', f(width));
          l.setAttribute('stroke-opacity', f(op * (1 - smooth)));
          l.setAttribute('stroke-linecap', isSel && u > 0.02 && (j === 0 || j === M - 1) ? 'round' : 'butt');
        }
        const D = pt((R + ko + 22) * shrink, (a0 + a1) / 2 + rot);
        dots[k].setAttribute('cx', f(D[0])); dots[k].setAttribute('cy', f(D[1]));
        dots[k].setAttribute('r', k === sel || st.hov === k ? '4.5' : n > 15 ? '2.5' : '3.5');
        dots[k].setAttribute('opacity', f(clamp01(1 - q * 1.8)));
      });
      // the labels ride round with their slices and fade as the ring goes
      const ringA = clamp01(1 - q * 1.8);
      labels.forEach((b, k) => {
        const mid = (seg[k][0] + seg[k][1]) / 2 + rot;
        const P = pt((R + go[k] + 36) * (1 - 0.2 * q), mid);
        const s = Math.sin(mid), co = Math.cos(mid);
        const tx = -50 + 50 * Math.max(-1, Math.min(1, s / 0.3)), ty = -50 - 50 * Math.max(-1, Math.min(1, co / 0.3));
        b.style.left = `${f(P[0])}px`;
        b.style.top = `${f(P[1])}px`;
        b.style.transform = `translate(${f(tx)}%, ${f(ty)}%)`;
        b.style.alignItems = s > 0.3 ? 'flex-start' : s < -0.3 ? 'flex-end' : 'center';
        b.style.textAlign = s > 0.3 ? 'left' : s < -0.3 ? 'right' : 'center';
        const shown = n <= 10 || k === st.sel || (k !== (st.sel + 1) % n && k !== (st.sel + n - 1) % n && k % crowd === 0 && !(k === n - 1 && n % crowd !== 0));
        b.classList.toggle('is-quiet', !shown);
        b.classList.toggle('is-hot', st.hov === k);
      });
      labelBox.style.opacity = f(ringA);
      centre.style.opacity = f(ringA);
      centre.style.transform = `translate(-50%, -50%) scale(${f(1 - 0.12 * q)})`;
      const ringOn = e < 0.3;
      labelBox.inert = !ringOn;
      hitG.style.pointerEvents = e < 0.02 ? 'auto' : 'none';
      // the bar's words come out from the bar once it is nearly straight, row by row
      const barA = span(u, 0.7, 1);
      head.style.opacity = f(barA);
      head.style.transform = `translateY(${f((1 - barA) * 8)}px)`;
      note.style.opacity = f(barA);
      rows.forEach((r, i) => {
        if (r.classList.contains('is-out')) return;
        const ra = span(u, 0.62 + 0.06 * i, 0.92 + 0.06 * i) * span(st.w, 0.25 + 0.08 * i, 0.75 + 0.08 * i);
        r.style.opacity = f(ra);
        r.style.setProperty('--rr-x', `${f((1 - ra) * -18)}px`);
      });
      ticks.forEach((t, i) => {
        const ta = span(u, 0.8 + 0.04 * i, 0.96 + 0.04 * i) * span(st.w, 0.3 + 0.06 * i, 0.8 + 0.06 * i);
        t.style.opacity = f(ta);
        t.style.transform = `scale(${f(0.2 + 0.8 * ta)})`;
      });
      leaders.style.opacity = f(barA * span(st.w, 0.4, 1));
      barBox.inert = u < 0.8;
      barBox.classList.toggle('is-live', u >= 0.8);
      ov.classList.toggle('is-bar', e > 0.5);
      ov.dataset.state = e >= 0.999 ? 'bar' : e <= 0.001 ? 'ring' : 'moving';
      foot.textContent = e > 0.5
        ? 'Height on the bar is points. Switch with the dots; the circle button or Esc goes back.'
        : m.graded ? 'The ring pushes out where you scored well and pulls in where you lost points.'
          : 'Each colour’s stretch is its share of the points. Pick one to open it.';
    }

    // ---- motion: one tween per value, cancelled before another starts so a quick press never leaves it half-way
    function tween(key, to, dur, done, delay = 0) {
      if (rafs[key]) cancelAnimationFrame(rafs[key]);
      rafs[key] = 0;
      if (reduce || dur <= 0) { st[key] = to; draw(); done?.(); return; }
      const from = st[key], t0 = performance.now() + delay;
      const step = (now) => {
        const k = clamp01((now - t0) / dur);
        st[key] = from + (to - from) * ease(k);
        draw();
        if (k < 1) rafs[key] = requestAnimationFrame(step);
        else { rafs[key] = 0; done?.(); }
      };
      rafs[key] = requestAnimationFrame(step);
    }
    function hover(k) {
      if (st.hov === k) return;
      st.hov = k;
      if (!rafs.t && !rafs.g) draw();
    }
    function select(k) {
      k = ((k % n) + n) % n;
      if (k === st.sel && st.t > 0.98) return;
      if (st.t > 0.98 && k !== st.sel) { // the bar is out: switch in place, the colour crossing over, the rows changing
        st.prev = st.sel;
        st.sel = k;
        st.w = 0;
        fillBar(k, true);
        tween('w', 1, 380);
        name.focus({ preventScroll: true });
        return;
      }
      if (st.t > 0.02 && k !== st.sel) { tween('t', 0, 380, () => select(k)); return; } // (part-way: back round first)
      st.sel = k;
      st.prev = -1;
      st.w = 1;
      fillBar(k, false);
      st.BB0 = st.BB;
      ov.focus({ preventScroll: true }); // (held by the ring itself while the label it was on goes out of reach)
      tween('t', 1, 900, () => name.focus({ preventScroll: true }));
    }
    function toRing() {
      if (st.t <= 0.001) return;
      const k = st.sel;
      ov.focus({ preventScroll: true });
      tween('t', 0, 720, () => labels[k].focus({ preventScroll: true }));
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
      for (const k in rafs) if (rafs[k]) cancelAnimationFrame(rafs[k]);
      removeEventListener('resize', onResize);
      if (live === api) live = null;
      const refocus = () => { try { if (from && from.isConnected) from.focus({ preventScroll: true }); } catch { /* gone */ } };
      if (now || reduce) { ov.remove(); refocus(); return; }
      ov.classList.add('is-closing', 'is-leaving');
      setTimeout(() => ov.remove(), 340);
      refocus();
    }
    /** Holds one tween value where it is put (the developer tools and the tests look at a frame part-way). */
    const seek = (key, v) => { if (rafs[key]) cancelAnimationFrame(rafs[key]); rafs[key] = 0; st[key] = v; draw(); };
    const api = { close, el: ov, select, toRing, seek, state: st };
    live = api;

    document.body.append(ov);
    addEventListener('resize', onResize);
    fit();
    fillBar(0, false);
    draw();
    // the ring blooms in; marked, it then bends to its marks
    if (m.graded && !reduce) tween('g', 1, 900, null, 380);
    ov.focus({ preventScroll: true });
    // a navigation away takes it too (app.js removes every sheet overlay): stop its frames
    const watch = new MutationObserver(() => { if (!ov.isConnected) { watch.disconnect(); if (!gone) close(true); } });
    watch.observe(document.body, { childList: true });
    return api;
  }

  BCV.rubricRing = { open, model, shortOf, get live() { return live; } };
})();
