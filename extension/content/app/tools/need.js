/* Grade needed: what it takes on the work still to come to finish with the grade wanted. Every
 * number comes from Canvas where it can — the course's current score (graded work only, as Canvas
 * counts it), the weight of each piece of work left (the assignment group's weight and the piece's
 * share of the group's points, or its share of the course's points where groups are not weighted)
 * — and the goal is a letter on the usual cut-offs (the GPA card's) or a number of your own. The sum
 * is the one everyone does by hand: the grade now, less what the piece will move it, over what the
 * piece is worth; the rest of the work is assumed to stay where it is. Out of reach and already
 * there are said plainly, and every letter's price is listed under the answer. */
(function () {
  const BCV = (self.BCV = self.BCV || {});
  const { h } = BCV.utils;
  const U = BCV.ui;
  const T = BCV.tools;

  const SCALE = [['A+', 97], ['A', 93], ['A−', 90], ['B+', 87], ['B', 83], ['B−', 80], ['C+', 77], ['C', 73], ['C−', 70], ['D', 60]];
  const r1 = (v) => Math.round(v * 10) / 10;
  const num = (v) => { const n = typeof v === 'number' ? v : parseFloat(String(v ?? '').replace('%', '')); return Number.isFinite(n) ? n : null; };
  /** The mark on work worth `worth`% of the final grade that lands `goal`% from `now`%, the rest of
   *  the work staying at `now`: { x, pct (rounded up), kind: 'ok' | 'over' (even 100% would not do it)
   *  | 'under' (even 0% would), ends100, ends0 } — or null when the numbers do not add up. */
  function needed(now, worth, goal) {
    if (![now, worth, goal].every((v) => typeof v === 'number' && Number.isFinite(v))) return null;
    const w = worth / 100;
    if (!(w > 0) || w > 1) return null;
    const x = (goal - now * (1 - w)) / w;
    return { x, pct: Math.max(0, Math.ceil(x - 1e-9)), kind: x > 100 ? 'over' : x <= 0 ? 'under' : 'ok', ends100: r1(now * (1 - w) + 100 * w), ends0: r1(now * (1 - w)) };
  }
  /** The work in a course not yet graded, each piece with its share of the final grade. A grade
   *  that is in but not posted counts as not in: Canvas leaves it out of the current score too. */
  function pieces(course, groups) {
    const all = (groups || []).flatMap((g) => (g.assignments || []).filter((a) => !a.omit_from_final_grade && Number(a.points_possible) > 0).map((a) => ({ a, g })));
    const total = all.reduce((s, { a }) => s + Number(a.points_possible), 0);
    const groupPts = {};
    for (const { a, g } of all) groupPts[g.id] = (groupPts[g.id] || 0) + Number(a.points_possible);
    const out = [];
    for (const { a, g } of all) {
      const sub = a.submission;
      const graded = !!sub && sub.score != null && sub.workflow_state === 'graded' && sub.posted_at !== null;
      if (graded) continue;
      const pts = Number(a.points_possible);
      const worth = course.weighted ? ((Number(g.group_weight) || 0) * pts) / (groupPts[g.id] || pts) : (100 * pts) / (total || pts);
      out.push({ id: String(a.id), name: a.name || 'Untitled', worth: r1(worth), pts, due: a.due_at ? Date.parse(a.due_at) : null, group: g.name || '', groupWeight: Number(g.group_weight) || 0, groupPts: groupPts[g.id] || pts, total });
    }
    return out.sort((x, y) => (y.worth - x.worth) || ((y.due || 0) - (x.due || 0)));
  }
  const nextLetter = (now) => { for (let i = SCALE.length - 1; i >= 0; i--) if (SCALE[i][1] > now) return SCALE[i]; return SCALE[0]; };
  const an = (letter) => (/^A/.test(letter) ? `an ${letter}` : `a ${letter}`);

  async function open(app, { from = null, now = null, worth = null, goal = null } = {}) {
    const tool = T.toolOf('need');
    const own = now !== null || worth !== null || goal !== null; // numbers handed in (the pin's quick menu): no course
    const st = { courses: [], course: own ? '' : null, pieces: [], piece: own ? 'custom' : '', now: now ?? '', worth: worth ?? '', goal: goal ?? '', letter: goal === null ? '' : 'custom', loading: false, note: '' };
    const body = U.el('bcv-need');
    const p = T.popup({ tool, title: 'Grade needed', sub: '', width: 620, body, from });
    // the fields, made once (typing in them must not redraw them)
    const field = (key, label, ph) => {
      const inp = T.input({ type: 'text', inputmode: 'decimal', placeholder: ph, 'aria-label': label, autocomplete: 'off', dataset: { field: key } });
      inp.value = st[key] === '' || st[key] === null ? '' : String(st[key]);
      inp.addEventListener('input', () => { st[key] = inp.value; if (key === 'goal') st.letter = 'custom'; paintResult(); });
      return { inp, el: U.el('bcv-need__field', [U.text('bcv-need__fieldlabel', label), U.el('bcv-need__pct', inp)]) };
    };
    const nowF = field('now', 'Your grade now', 'e.g. 88');
    const worthF = field('worth', 'Worth this much of the final grade', 'e.g. 25');
    const goalF = field('goal', 'The grade you want', 'e.g. 90');
    const courseBox = U.el('bcv-need__pick');
    const nowHint = T.hint('');
    const pieceBox = U.el('bcv-need__pick');
    const pieceHint = T.hint('');
    const goalBox = U.el('bcv-need__pick');
    const result = U.el('bcv-tool__card bcv-need__result');
    const rows = U.el('bcv-need__rows');
    const noteEl = T.note('', 'warn');
    body.append(...T.rise([
      T.card([T.label('Course'), courseBox], 'bcv-need__course'),
      U.el('bcv-need__grid', [T.card([T.label('Now'), nowF.el, nowHint]), T.card([T.label('What is left'), pieceBox, worthF.el, pieceHint])]),
      T.card([T.label('Goal'), U.el('bcv-need__grid', [U.el('bcv-need__field', [U.text('bcv-need__fieldlabel', 'A letter'), goalBox]), goalF.el])]),
      noteEl,
      result,
      T.card([T.label('Every letter'), rows]),
      T.hint('Assumes the rest of your work stays at your grade now. Canvas counts graded work only, so this is the mark on that one piece with everything else as it is.'),
    ]));

    const courseOf = () => st.courses.find((c) => String(c.id) === String(st.course)) || null;
    const pieceOf = () => st.pieces.find((x) => x.id === st.piece) || null;
    async function loadCourses() {
      let cs = [];
      try { cs = await BCV.store.courses(); } catch { cs = []; }
      if (!p.alive()) return;
      const current = cs.filter((c) => c.state === 'current');
      const favs = current.filter((c) => c.favorite);
      st.courses = (favs.length ? favs : current).slice().sort((a, b) => a.name.localeCompare(b.name));
      if (st.course === null) {
        const here = /\/courses\/(\d+)/.exec(location.pathname)?.[1];
        const pick = st.courses.find((c) => String(c.id) === here) || st.courses.find((c) => c.score !== null) || st.courses[0] || null;
        if (pick) await pickCourse(String(pick.id)); else { st.course = ''; st.piece = 'custom'; paint(); }
      } else paint();
    }
    async function pickCourse(id) {
      st.course = id;
      const c = courseOf();
      st.pieces = [];
      st.piece = 'custom';
      if (c) {
        st.now = c.score === null || c.score === undefined ? '' : String(r1(Number(c.score)));
        nowF.inp.value = st.now;
        const n = num(st.now);
        if (n !== null) { const [letter, cut] = nextLetter(n); st.letter = letter; st.goal = String(cut); goalF.inp.value = st.goal; }
        st.loading = true;
        paint();
        let groups = [];
        try { groups = await BCV.store.assignmentGroups(id); } catch { groups = []; }
        if (!p.alive() || st.course !== id) return;
        st.loading = false;
        st.pieces = pieces(c, groups);
        const first = st.pieces.find((x) => x.worth > 0);
        if (first) { st.piece = first.id; st.worth = String(first.worth); worthF.inp.value = st.worth; }
      }
      paint();
    }
    function paint() {
      const c = courseOf();
      p.setSub(c ? 'Every number from Canvas: your score, the weights, the points.' : 'Your own numbers.');
      courseBox.replaceChildren(U.picker([...st.courses.map((x) => ({ value: String(x.id), text: x.name })), { value: '', text: 'Not from Canvas — my own numbers' }], st.course ?? '', (v) => { if (v) pickCourse(v); else { st.course = ''; st.pieces = []; st.piece = 'custom'; paint(); } }, { label: 'Course', placeholder: 'Choose a course' }));
      nowHint.textContent = c ? (c.score === null ? 'Canvas has no score for this course yet.' : 'From Canvas: your current score, graded work only.') : 'Your current grade, as a percentage.';
      const opts = [...st.pieces.map((x) => ({ value: x.id, text: `${x.name} · ${x.worth}%` })), { value: 'custom', text: 'Something else…' }];
      pieceBox.hidden = !c;
      pieceBox.replaceChildren(c ? U.picker(opts, st.piece, (v) => { st.piece = v; const x = pieceOf(); if (x) { st.worth = String(x.worth); worthF.inp.value = st.worth; } paint(); }, { label: 'What is left', placeholder: st.loading ? 'Reading the assignments…' : 'Choose what is left' }) : null);
      const x = pieceOf();
      worthF.el.hidden = !!x;
      pieceHint.textContent = x ? (c?.weighted ? `${x.group} is ${x.groupWeight}% of the grade; this is ${U.plural(x.pts, 'point')} of the group's ${x.groupPts}.` : `${U.plural(x.pts, 'point')} of the course's ${x.total}.`) : st.loading ? 'Reading the assignments…' : c && !st.pieces.length ? 'Nothing left ungraded that Canvas knows of. Type what the work is worth.' : 'How much of the final grade the work still to come is worth.';
      goalBox.replaceChildren(U.picker([...SCALE.map(([l, cut]) => ({ value: l, text: `${l} · ${cut}%` })), { value: 'custom', text: 'A number of my own' }], st.letter || 'custom', (v) => { st.letter = v; const s = SCALE.find(([l]) => l === v); if (s) { st.goal = String(s[1]); goalF.inp.value = st.goal; } paintResult(); }, { label: 'The grade you want', placeholder: 'Choose a letter' }));
      paintResult();
    }
    function paintResult() {
      const nowV = num(st.now), worthV = num(st.worth), goalV = num(st.goal);
      const x = pieceOf();
      const where = x ? `on ${x.name}` : 'on what is left';
      const r = needed(nowV, worthV, goalV);
      const letter = SCALE.find(([l]) => l === st.letter && Number(l && st.goal) === SCALE.find(([m]) => m === l)?.[1]) ? st.letter : null;
      const goalWord = letter ? `${an(letter)} (${goalV}%)` : goalV === null ? 'your goal' : `${goalV}%`;
      noteEl.hidden = !(worthV !== null && (worthV <= 0 || worthV > 100));
      noteEl.textContent = 'The work left has to be worth something between 0 and 100% of the grade.';
      result.replaceChildren(
        U.text(`bcv-need__big ${r ? `is-${r.kind}` : ''}`, !r ? '—' : r.kind === 'over' ? 'Out of reach' : r.kind === 'under' ? 'Already there' : `${r.pct}%`),
        U.text('bcv-need__line bcv-pretty', !r ? 'Fill in the three numbers.' : r.kind === 'ok' ? `${where} to finish with ${goalWord}.` : r.kind === 'over' ? `Even 100% ${where} ends at ${r.ends100}%. Aim for a goal under that.` : `Even 0% ${where} leaves you at ${r.ends0}%.`),
      );
      const list = [];
      if (nowV !== null && worthV !== null) {
        for (const [l, cut] of SCALE) {
          const q = needed(nowV, worthV, cut);
          if (!q) break;
          list.push(h('div', { class: `bcv-need__row ${q.kind === 'over' ? 'is-over' : ''} ${l === st.letter ? 'is-goal' : ''}` }, [h('span', { text: l }), h('span', { text: q.kind === 'over' ? 'out of reach' : q.kind === 'under' ? 'already there' : `${q.pct}%` })]));
          if (q.kind === 'under') break;
        }
      }
      rows.replaceChildren(...(list.length ? list : [U.text('bcv-tool__hint', 'The price of each letter shows once the numbers are in.')]));
    }
    paint();
    loadCourses();
    return p;
  }

  BCV.toolsNeed = { open, needed, pieces, SCALE };
})();
